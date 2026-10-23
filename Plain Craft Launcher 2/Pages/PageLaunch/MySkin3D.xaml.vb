Imports System.Windows.Media.Media3D
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports System.Windows.Threading

Public Class MySkin3D

    '点击（与 MySkin 保持一致，供登录卡片的 Handles Skin.Click 使用）
    Public Event Click(sender As Object, e As MouseButtonEventArgs)

    '皮肤加载器
    Private _Loader As LoaderTask(Of (String, String), String)
    Public Property Loader As LoaderTask(Of (String, String), String)
        Get
            Return _Loader
        End Get
        Set(value As LoaderTask(Of (String, String), String))
            _Loader = value
            If SkinHelper IsNot Nothing Then SkinHelper.Loader = value
        End Set
    End Property

    '地址（仅用于 ToolTip，与 MySkin 行为一致）
    Private _Address As String = ""
    Public Property Address As String
        Get
            Return _Address
        End Get
        Set(value As String)
            _Address = value
            ToolTip = If(_Address = "", "加载中", "点击更换皮肤（右键查看更多选项）")
        End Set
    End Property

    '是否显示披风（与 MySkin 一致）
    Private _HasCape As Boolean = False
    Public Property HasCape As Boolean
        Get
            Return _HasCape
        End Get
        Set(value As Boolean)
            _HasCape = value
            If SkinHelper IsNot Nothing Then SkinHelper.HasCape = value
        End Set
    End Property

    '==== 3D 旋转动画 ====
    '缓慢自转（约 10°/s），拖动时暂停；模型为空或控件卸载时停表，避免空转
    Private WithEvents RotateTimer As New DispatcherTimer()
    Private Const RotateSpeed As Double = 0.35   '每帧角度（33ms/帧，约 10°/s）

    Private Sub MySkin3D_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        RotateTimer.Interval = TimeSpan.FromMilliseconds(33)
        RotateTimer.Start()
    End Sub
    Private Sub MySkin3D_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        RotateTimer.Stop()
    End Sub
    Private Sub RotateTimer_Tick(sender As Object, e As EventArgs) Handles RotateTimer.Tick
        If Group Is Nothing OrElse Group.Children.Count = 0 Then Return
        SpinY.Angle = (SpinY.Angle + RotateSpeed) Mod 360
    End Sub

    '==== 鼠标拖动旋转（Yaw + Pitch）====
    Private isDragging As Boolean = False
    Private dragLastX As Double = 0
    Private dragLastY As Double = 0
    Private dragMoved As Boolean = False

    Private Sub MySkin3D_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonDown
        isDragging = True
        dragMoved = False
        dragLastX = e.GetPosition(Me).X
        dragLastY = e.GetPosition(Me).Y
        RotateTimer.Stop()      '拖动时暂停自动旋转
        CaptureMouse()
    End Sub

    Private Sub MySkin3D_MouseMove(sender As Object, e As MouseEventArgs) Handles Me.MouseMove
        If Not isDragging Then Return
        Dim p = e.GetPosition(Me)
        Dim dx = p.X - dragLastX
        Dim dy = p.Y - dragLastY
        dragLastX = p.X
        dragLastY = p.Y
        If Math.Abs(dx) > 1 OrElse Math.Abs(dy) > 1 Then dragMoved = True
        '水平拖动 -> 绕 Y 轴（Yaw）；垂直拖动 -> 绕 X 轴（Pitch，限制范围避免翻转）
        SpinY.Angle = (SpinY.Angle + dx * 0.5) Mod 360
        SpinX.Angle = Math.Max(-75, Math.Min(75, SpinX.Angle - dy * 0.4))
    End Sub

    Private Sub MySkin3D_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonUp
        If Not isDragging Then Return
        isDragging = False
        ReleaseMouseCapture()
        '只有“未拖动”的释放才算一次点击（用于更换皮肤）
        If Not dragMoved Then RaiseEvent Click(Me, e)
        RotateTimer.Start()     '松手后恢复自动旋转
    End Sub

    '==== 载入 3D 皮肤 ====
    '皮肤贴图的原始像素与缩放比（第二层要做逐像素裁剪，必须拿到 alpha）
    Private _Scale As Integer = 1
    Private _TexBytes() As Byte
    Private _TexStride As Integer

    Public Sub Load()
        Try
            If _Loader Is Nothing Then Return
            Address = _Loader.Output
            If String.IsNullOrEmpty(Address) Then Throw New Exception("皮肤加载器没有输出")
            If Not Address.StartsWithF(PathImage) AndAlso Not FileUtils.Exists(Address) Then Throw New FileNotFoundException("皮肤文件未找到", Address)
            Dim bmp As New MyBitmap(Address)
            Dim texW As Integer = bmp.Pic.Width
            Dim texH As Integer = bmp.Pic.Height
            '皮肤可能被放大保存（128x128、512x512……），统一换算回 64 格逻辑坐标
            _Scale = Math.Max(1, CInt(texW / 64.0))
            '旧版 64x32 皮肤没有左臂 / 左腿贴图，也没有第二层
            Dim isLegacy As Boolean = texH / _Scale < 64
            BuildModel(ReadTexture(bmp.Pic), isLegacy)
            Logger.Info($"载入 3D 皮肤成功：{If(_Loader.Name, "null")}（{texW}x{texH}，{Group.Children.Count} 个面）")
        Catch ex As Exception
            Group.Children.Clear()
            Logger.Error(ex, "载入 3D 皮肤失败（" & If(_Address, "null") & "）", LogBehavior.Toast)
        End Try
    End Sub

    ''' <summary>
    ''' 把 GDI+ 位图拷成独立的 BitmapSource，同时缓存原始像素供 alpha 裁剪使用。
    ''' 不能直接用 MyBitmap 的转换操作符：它把 Scan0 指针直接交给 BitmapSource，位图一旦回收就会读到脏数据。
    ''' DPI 固定 96，取图用的是归一化 Viewbox，不受系统缩放影响。
    ''' </summary>
    Private Function ReadTexture(pic As System.Drawing.Bitmap) As BitmapSource
        Dim rect = New System.Drawing.Rectangle(0, 0, pic.Width, pic.Height)
        Dim data = pic.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb)
        Try
            _TexStride = data.Stride
            ReDim _TexBytes(data.Stride * pic.Height - 1)
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, _TexBytes, 0, _TexBytes.Length)
            Dim bs = BitmapSource.Create(pic.Width, pic.Height, 96, 96, PixelFormats.Bgra32, Nothing, _TexBytes, data.Stride)
            bs.Freeze()
            Return bs
        Finally
            pic.UnlockBits(data)
        End Try
    End Function

    ''' <summary>取 64 格逻辑坐标处的 alpha（已按 _Scale 换算成真实像素）</summary>
    Private Function Alpha(x As Integer, y As Integer) As Integer
        Return CInt(_TexBytes(y * _Scale * _TexStride + x * _Scale * 4 + 3))
    End Function


    Public Sub Clear()
        Group.Children.Clear()
    End Sub

    '==== 构建角色模型 ====
    '标准 64x64 皮肤分块（1 单位 = 1 皮肤像素）：
    '  头部 基础(0,0)    帽子层(32,0)
    '  右腿 基础(0,16)   裤子层(0,32)
    '  身体 基础(16,16)  外套层(16,32)
    '  右臂 基础(40,16)  袖子层(40,32)
    '  左腿 基础(16,48)  裤子层(0,48)
    '  左臂 基础(32,48)  袖子层(48,48)
    '旧版 64x32 皮肤只画了右半边分块，左臂 / 左腿用右侧贴图镜像，且没有第二层。
    Private Sub BuildModel(src As ImageSource, IsLegacy As Boolean)
        Group.Children.Clear()

        '基础层
        AddBoxFaces(-4, 4, 24, 32, -4, 4, FacesOfBlock(0, 0, 8, 8, 8), src)                              '头部 8x8x8
        AddBoxFaces(-4, 4, 12, 24, -2, 2, FacesOfBlock(16, 16, 8, 12, 4), src)                           '身体 8x12x4
        AddBoxFaces(-8, -4, 12, 24, -2, 2, FacesOfBlock(40, 16, 4, 12, 4), src)                          '右臂（-X 侧）
        AddBoxFaces(4, 8, 12, 24, -2, 2, If(IsLegacy, FacesOfBlock(40, 16, 4, 12, 4),
                                                            FacesOfBlock(32, 48, 4, 12, 4)), src)     '左臂（+X 侧）
        AddBoxFaces(-4, 0, 0, 12, -2, 2, FacesOfBlock(0, 16, 4, 12, 4), src)                             '右腿（-X 侧）
        AddBoxFaces(0, 4, 0, 12, -2, 2, If(IsLegacy, FacesOfBlock(0, 16, 4, 12, 4),
                                                           FacesOfBlock(16, 48, 4, 12, 4)), src)     '左腿（+X 侧）

        '第二层（帽子 / 外套 / 袖子 / 裤子）：外扩一点，避免和基础层共面闪烁。
        'WPF 3D 没有 alpha 裁剪，直接画半透明盒子会让透明像素写进深度缓冲、在脑袋上扎出破洞，
        '所以这里把每个面按像素 alpha 打散成小块网格（AddCutout），透明处是真的不画。
        If Not IsLegacy AndAlso _TexBytes IsNot Nothing Then
            AddOverlay(-4.5, 4.5, 23.5, 32.5, -4.5, 4.5, 32, 0, 8, 8, 8, src)              '帽子（外扩 0.5）
            AddOverlay(-4.25, 4.25, 11.75, 24.25, -2.25, 2.25, 16, 32, 8, 12, 4, src)        '外套（外扩 0.25）
            AddOverlay(-8.25, -3.75, 11.75, 24.25, -2.25, 2.25, 40, 32, 4, 12, 4, src)       '右袖
            AddOverlay(3.75, 8.25, 11.75, 24.25, -2.25, 2.25, 48, 48, 4, 12, 4, src)         '左袖
            AddOverlay(-4.25, 0.25, -0.25, 12.25, -2.25, 2.25, 0, 32, 4, 12, 4, src)         '右裤
            AddOverlay(-0.25, 4.25, -0.25, 12.25, -2.25, 2.25, 0, 48, 4, 12, 4, src)         '左裤
        End If
    End Sub

    ''' <summary>
    ''' 画第二层的一个盒子：六个面都按像素 alpha 裁剪。
    ''' blockX/blockY 是该部件第二层分块在 64x64 皮肤里的左上角。
    ''' </summary>
    Private Sub AddOverlay(x0 As Double, x1 As Double, y0 As Double, y1 As Double, z0 As Double, z1 As Double,
                           blockX As Integer, blockY As Integer, w As Integer, h As Integer, d As Integer, src As ImageSource)
        AddCutout(New Point3D(x0, y1, z0), New Point3D(x1, y1, z0), New Point3D(x1, y1, z1), New Point3D(x0, y1, z1),
                  blockX + d, blockY, w, d, New Vector3D(0, 1, 0), src)                     '顶面
        AddCutout(New Point3D(x0, y0, z1), New Point3D(x1, y0, z1), New Point3D(x1, y0, z0), New Point3D(x0, y0, z0),
                  blockX + d + w, blockY, w, d, New Vector3D(0, -1, 0), src)                '底面
        AddCutout(New Point3D(x0, y1, z1), New Point3D(x1, y1, z1), New Point3D(x1, y0, z1), New Point3D(x0, y0, z1),
                  blockX + d, blockY + d, w, h, New Vector3D(0, 0, 1), src)                 '正面
        AddCutout(New Point3D(x1, y1, z0), New Point3D(x0, y1, z0), New Point3D(x0, y0, z0), New Point3D(x1, y0, z0),
                  blockX + 2 * d + w, blockY + d, w, h, New Vector3D(0, 0, -1), src)        '背面
        AddCutout(New Point3D(x1, y1, z1), New Point3D(x1, y1, z0), New Point3D(x1, y0, z0), New Point3D(x1, y0, z1),
                  blockX + 2 * d, blockY + d, d, h, New Vector3D(1, 0, 0), src)             '左侧面（+X）
        AddCutout(New Point3D(x0, y1, z0), New Point3D(x0, y1, z1), New Point3D(x0, y0, z1), New Point3D(x0, y0, z0),
                  blockX, blockY + d, d, h, New Vector3D(-1, 0, 0), src)                    '右侧面（-X）
    End Sub

    ''' <summary>
    ''' 按像素 alpha 生成一个面：只把不透明像素（含水平连续段）拼成网格，等价于做了一次 alpha 裁剪。
    ''' p0~p3 依次对应 UV 的 (0,0)/(1,0)/(1,1)/(0,1)。
    ''' </summary>
    Private Sub AddCutout(p0 As Point3D, p1 As Point3D, p2 As Point3D, p3 As Point3D,
                          rx As Integer, ry As Integer, rw As Integer, rh As Integer, normal As Vector3D, src As ImageSource)
        Dim mesh As New MeshGeometry3D()
        Dim any As Boolean = False
        For j As Integer = 0 To rh - 1
            Dim i As Integer = 0
            While i < rw
                If Alpha(rx + i, ry + j) < 128 Then
                    i += 1
                    Continue While
                End If
                Dim i0 = i
                While i < rw AndAlso Alpha(rx + i, ry + j) >= 128
                    i += 1
                End While
                Dim i1 = i - 1
                Dim u0 = i0 / rw : Dim u1 = (i1 + 1) / rw
                Dim v0 = j / rh : Dim v1 = (j + 1) / rh
                Dim k = mesh.Positions.Count
                mesh.Positions.Add(Lerp(p0, p1, p3, u0, v0))
                mesh.Positions.Add(Lerp(p0, p1, p3, u1, v0))
                mesh.Positions.Add(Lerp(p0, p1, p3, u1, v1))
                mesh.Positions.Add(Lerp(p0, p1, p3, u0, v1))
                mesh.TriangleIndices.Add(k) : mesh.TriangleIndices.Add(k + 1) : mesh.TriangleIndices.Add(k + 2)
                mesh.TriangleIndices.Add(k) : mesh.TriangleIndices.Add(k + 2) : mesh.TriangleIndices.Add(k + 3)
                Dim tu0 = (rx + u0 * rw) / 64.0 : Dim tu1 = (rx + u1 * rw) / 64.0
                Dim tv0 = (ry + v0 * rh) / 64.0 : Dim tv1 = (ry + v1 * rh) / 64.0
                mesh.TextureCoordinates.Add(New Point(tu0, tv0))
                mesh.TextureCoordinates.Add(New Point(tu1, tv0))
                mesh.TextureCoordinates.Add(New Point(tu1, tv1))
                mesh.TextureCoordinates.Add(New Point(tu0, tv1))
                For n As Integer = 1 To 4
                    mesh.Normals.Add(normal)
                Next
                any = True
            End While
        Next
        If Not any Then Return
        Dim brush As New ImageBrush(src) With {
            .Viewbox = New Rect(0, 0, 1, 1),
            .ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            .Stretch = Stretch.Fill
        }
        Dim mat As New DiffuseMaterial(brush)
        Group.Children.Add(New GeometryModel3D(mesh, mat) With {.BackMaterial = mat})
    End Sub

    Private Function Lerp(p0 As Point3D, p1 As Point3D, p3 As Point3D, u As Double, v As Double) As Point3D
        Return New Point3D(p0.X + u * (p1.X - p0.X) + v * (p3.X - p0.X),
                           p0.Y + u * (p1.Y - p0.Y) + v * (p3.Y - p0.Y),
                           p0.Z + u * (p1.Z - p0.Z) + v * (p3.Z - p0.Z))
    End Function

    ''' <summary>
    ''' 按标准皮肤分块，算出六个面的取图矩形。
    ''' 分块内布局：上方并排是顶面 / 底面，下面一行依次是 右侧 / 正面 / 左侧 / 背面。
    ''' </summary>
    Private Function FacesOfBlock(blockX As Integer, blockY As Integer, w As Integer, h As Integer, d As Integer) As Rect()
        Return New Rect() {
            MK(blockX + d, blockY, w, d),                '顶面
            MK(blockX + d + w, blockY, w, d),            '底面
            MK(blockX, blockY + d, d, h),                '右侧面（-X）
            MK(blockX + d, blockY + d, w, h),            '正面（+Z）
            MK(blockX + 2 * d, blockY + d, d, h),        '左侧面（+X）
            MK(blockX + 2 * d + w, blockY + d, w, h)     '背面（-Z）
        }
    End Function


    Private Function MK(x As Double, y As Double, w As Double, h As Double) As Rect
        '皮肤贴图始终按 64x64 的逻辑网格取图，再归一化成 0~1。
        '不能用绝对像素坐标：MyBitmap 转出来的 BitmapSource DPI 随系统变化（120 DPI 下 64px = 51.2 DIP），
        '绝对视图框会让 UV 整体偏移、采到透明区，模型就会“碎成一块块”。
        Return New Rect(x / 64, y / 64, w / 64, h / 64)
    End Function

    Private Sub AddBoxFaces(x0 As Double, x1 As Double, y0 As Double, y1 As Double, z0 As Double, z1 As Double,
                            faces As Rect(), src As ImageSource)
        AddBox(x0, x1, y0, y1, z0, z1, faces(0), faces(1), faces(2), faces(3), faces(4), faces(5), src)
    End Sub

    ''' <summary>
    ''' 生成一个长方体。六个贴图参数依次是：顶面、底面、右侧面、正面、左侧面、背面。
    ''' 注意：角色面向 +Z（朝向镜头），所以 +X 是角色自己的“左”，-X 才是“右”。
    ''' </summary>
    Private Sub AddBox(x0 As Double, x1 As Double, y0 As Double, y1 As Double, z0 As Double, z1 As Double,
                       top As Rect, bottom As Rect, rightSide As Rect, front As Rect, leftSide As Rect, back As Rect, src As ImageSource)
        AddFace(New Point3D(x0, y1, z0), New Point3D(x1, y1, z0), New Point3D(x1, y1, z1), New Point3D(x0, y1, z1), top, New Vector3D(0, 1, 0), src)
        AddFace(New Point3D(x0, y0, z1), New Point3D(x1, y0, z1), New Point3D(x1, y0, z0), New Point3D(x0, y0, z0), bottom, New Vector3D(0, -1, 0), src)
        AddFace(New Point3D(x0, y1, z1), New Point3D(x1, y1, z1), New Point3D(x1, y0, z1), New Point3D(x0, y0, z1), front, New Vector3D(0, 0, 1), src)
        AddFace(New Point3D(x1, y1, z0), New Point3D(x0, y1, z0), New Point3D(x0, y0, z0), New Point3D(x1, y0, z0), back, New Vector3D(0, 0, -1), src)
        AddFace(New Point3D(x1, y1, z1), New Point3D(x1, y1, z0), New Point3D(x1, y0, z0), New Point3D(x1, y0, z1), leftSide, New Vector3D(1, 0, 0), src)
        AddFace(New Point3D(x0, y1, z0), New Point3D(x0, y1, z1), New Point3D(x0, y0, z1), New Point3D(x0, y0, z0), rightSide, New Vector3D(-1, 0, 0), src)
    End Sub

    Private Sub AddFace(p0 As Point3D, p1 As Point3D, p2 As Point3D, p3 As Point3D, rect As Rect, normal As Vector3D, src As ImageSource)
        Dim mesh As New MeshGeometry3D()
        mesh.Positions.Add(p0)
        mesh.Positions.Add(p1)
        mesh.Positions.Add(p2)
        mesh.Positions.Add(p3)
        mesh.TriangleIndices.Add(0)
        mesh.TriangleIndices.Add(1)
        mesh.TriangleIndices.Add(2)
        mesh.TriangleIndices.Add(0)
        mesh.TriangleIndices.Add(2)
        mesh.TriangleIndices.Add(3)
        mesh.TextureCoordinates.Add(New Point(0, 0))
        mesh.TextureCoordinates.Add(New Point(1, 0))
        mesh.TextureCoordinates.Add(New Point(1, 1))
        mesh.TextureCoordinates.Add(New Point(0, 1))
        mesh.Normals.Add(normal)
        mesh.Normals.Add(normal)
        mesh.Normals.Add(normal)
        mesh.Normals.Add(normal)
        Dim brush As New ImageBrush(src) With {
            .Viewbox = rect,
            .ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            .Stretch = Stretch.Fill
        }
        Dim mat As New DiffuseMaterial(brush)
        Dim gm As New GeometryModel3D(mesh, mat) With {.BackMaterial = mat}
        Group.Children.Add(gm)
    End Sub

    '==== 复用 MySkin 的静态逻辑，保留“保存 / 刷新 / 披风”功能 ====
    Public Sub BtnSkinSave_Click()
        If _Loader Is Nothing Then Return
        MySkin.Save(_Loader)
    End Sub
    Public Sub RefreshClick()
        If _Loader Is Nothing Then Return
        MySkin.RefreshCache(_Loader)
    End Sub
    Public Sub BtnSkinCape_Click()
        SkinHelper.BtnSkinCape_Click()
    End Sub

End Class
