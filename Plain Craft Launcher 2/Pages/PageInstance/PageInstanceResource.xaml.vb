''' <summary>
''' 版本实例的资源管理页：集中管理存档、截图、资源包、光影包、投影原理图与服务器列表。
''' 参照 PCL-CE 的 PageInstanceCompResource 精简而来，先覆盖本地文件的查看与清理。
''' </summary>
Public Class PageInstanceResource

    ''' <summary>
    ''' 资源分类定义。Folder 为版本目录下的相对路径；Nothing 表示读取 servers.dat 的服务器列表。
    ''' </summary>
    Private Class ResourceCategory
        Public Title As String
        Public Folder As String
        Public EmptyInfo As String
    End Class

    Private ReadOnly Categories As ResourceCategory() = {
        New ResourceCategory With {.Title = "存档", .Folder = "saves\", .EmptyInfo = "下载或创建世界后，世界文件夹会出现在这里。"},
        New ResourceCategory With {.Title = "截图", .Folder = "screenshots\", .EmptyInfo = "在游戏中按 F2 截图后，图片会出现在这里。"},
        New ResourceCategory With {.Title = "资源包", .Folder = "resourcepacks\", .EmptyInfo = "把资源包放进 resourcepacks 文件夹，或在游戏中选择后即可看到。"},
        New ResourceCategory With {.Title = "光影包", .Folder = "shaderpacks\", .EmptyInfo = "安装 Iris 或 OptiFine 后，把光影包放进 shaderpacks 文件夹即可。"},
        New ResourceCategory With {.Title = "投影原理图", .Folder = "schematics\", .EmptyInfo = "使用投影（Litematica）模组保存的原理图会出现在这里。"},
        New ResourceCategory With {.Title = "服务器", .Folder = Nothing, .EmptyInfo = "在多人游戏中添加过的服务器会记录在 servers.dat 里。"}
    }

    ''' <summary>
    ''' 当前选中的分类下标。
    ''' </summary>
    Private CurrentType As Integer = 0

    Private Sub PageInstanceResource_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        Refresh()
    End Sub

    ''' <summary>
    ''' 当前分类对应的完整目录；服务器分类返回版本根目录。
    ''' </summary>
    Private Function CurrentPath() As String
        Dim Cat = Categories(CurrentType)
        If PageInstanceLeft.Instance Is Nothing Then Return ""
        Return If(Cat.Folder Is Nothing, PageInstanceLeft.Instance.PathIndie, PageInstanceLeft.Instance.PathIndie & Cat.Folder)
    End Function

    ''' <summary>
    ''' 刷新当前分类的列表。
    ''' </summary>
    Public Sub Refresh()
        If PageInstanceLeft.Instance Is Nothing Then Return
        Dim Cat = Categories(CurrentType)
        AniControlEnabled += 1
        Try
            PanList.Children.Clear()
            Dim Entries As Integer = 0
            If Cat.Folder IsNot Nothing Then
                '文件 / 文件夹类
                Dim Dir As String = CurrentPath()
                If Directory.Exists(Dir) Then
                    For Each Entry As String In Directory.GetFileSystemEntries(Dir)
                        Try
                            Dim Info As New FileInfo(Entry)
                            Dim IsFolder As Boolean = (Info.Attributes And FileAttributes.Directory) > 0
                            Dim SizeText As String = "文件夹"
                            If Not IsFolder Then SizeText = GetSizeText(Info.Length)
                            Dim NameText As String = IO.Path.GetFileName(Entry.TrimEnd("\"c))
                            PanList.Children.Add(MakeItem(NameText, SizeText & " · 修改于 " & Info.LastWriteTime.ToString("yyyy/MM/dd HH:mm"),
                                                          If(IsFolder, "文件夹", "文件"), Entry, True))
                            Entries += 1
                        Catch ex As Exception
                            Logger.Warn(ex, "列出资源条目失败：" & Entry)
                        End Try
                    Next
                End If
            Else
                '服务器列表（servers.dat）
                Dim DatPath As String = CurrentPath() & "servers.dat"
                If File.Exists(DatPath) Then
                    For Each Server As KeyValuePair(Of String, String) In ReadServersDat(DatPath)
                        PanList.Children.Add(MakeItem(Server.Key, Server.Value, "服务器", DatPath, False))
                        Entries += 1
                    Next
                End If
            End If
            '列表与空态切换
            If Entries = 0 Then
                PanListBack.Visibility = Visibility.Collapsed
                PanEmpty.Visibility = Visibility.Visible
                LabEmptyTitle.Text = $"暂无{Cat.Title}"
                LabEmptyInfo.Text = Cat.EmptyInfo
                BtnOpen.Visibility = If(Cat.Folder Is Nothing, Visibility.Collapsed, Visibility.Visible)
            Else
                PanListBack.Visibility = Visibility.Visible
                PanEmpty.Visibility = Visibility.Collapsed
                PanListBack.Title = $"{Cat.Title}（{Entries}）"
                BtnOpen.Visibility = If(Cat.Folder Is Nothing, Visibility.Collapsed, Visibility.Visible)
            End If
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub

    ''' <summary>
    ''' 生成一个资源条目。
    ''' </summary>
    Private Function MakeItem(Title As String, Info As String, TypeName As String, Target As String, CanDelete As Boolean) As MyListItem
        Dim NewItem As New MyListItem With {
            .Title = Title, .Info = Info, .Type = MyListItem.CheckType.Clickable,
            .Logo = Logo.IconButtonInfo, .LogoScale = 1.05}
        Dim BtnOpen As New MyIconButton With {.LogoScale = 1.05, .Logo = Logo.IconButtonOpen, .Tag = Target}
        BtnOpen.ToolTip = "打开文件位置"
        AddHandler BtnOpen.Click, Sub(ss, ee) OpenTarget(CStr(ss.Tag))
        Dim BtnDelete As New MyIconButton With {.LogoScale = 1, .Logo = Logo.IconButtonDelete, .Tag = NewItem}
        BtnDelete.ToolTip = "删除"
        AddHandler BtnDelete.Click, AddressOf Delete_Click
        If CanDelete Then
            NewItem.Buttons = {BtnOpen, BtnDelete}
        Else
            NewItem.Buttons = {BtnOpen}
        End If
        AddHandler NewItem.Click, Sub() OpenTarget(Target)
        Return NewItem
    End Function

    ''' <summary>
    ''' 用资源管理器打开文件 / 文件夹。
    ''' </summary>
    Private Sub OpenTarget(Target As String)
        Try
            If Directory.Exists(Target) Then
                Process.Start("explorer.exe", $"/select,""{Target.TrimEnd("\"c)}""")
            ElseIf File.Exists(Target) Then
                Process.Start("explorer.exe", $"/select,""{Target}""")
            Else
                Hint("文件已不存在，点击刷新查看最新内容", HintType.Red)
            End If
        Catch ex As Exception
            Logger.Error(ex, "打开文件位置失败", LogBehavior.Alert)
        End Try
    End Sub

    ''' <summary>
    ''' 删除资源：移入回收站而非永久删除。
    ''' </summary>
    Private Sub Delete_Click(sender As Object, e As EventArgs)
        Dim Item As MyListItem = CType(sender.Tag, MyListItem)
        If MyMsgBox($"确定要删除「{Item.Title}」吗？该操作会将文件移入回收站。", "删除确认",, "取消", IsWarn:=True) <> 1 Then Return
        Dim Cat = Categories(CurrentType)
        Dim Target As String = If(Cat.Folder Is Nothing, "", CurrentPath() & Item.Title)
        Try
            If Cat.Folder IsNot Nothing Then
                Dim Dir As String = CurrentPath() & Item.Title
                If Directory.Exists(Dir) Then
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(Dir, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin)
                ElseIf File.Exists(Dir) Then
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(Dir, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin)
                End If
            End If
            Hint("已删除：" & Item.Title, Log:=False)
            Refresh()
        Catch ex As Exception
            Logger.Error(ex, "删除资源失败", LogBehavior.Alert)
        End Try
    End Sub

    ''' <summary>
    ''' 解析 servers.dat，返回服务器名称与地址。
    ''' servers.dat 是未压缩的 NBT：根 Compound 的 servers 列表内每个 Compound 含 name/ip 字符串。
    ''' </summary>
    Private Shared Function ReadServersDat(Path As String) As List(Of KeyValuePair(Of String, String))
        Dim Result As New List(Of KeyValuePair(Of String, String))
        Dim Data As Byte() = File.ReadAllBytes(Path)
        If Data.Length < 5 OrElse Data(0) <> 10 Then Return Result
        '跳过根 Compound 的类型与名称
        Dim Pos As Integer = 3 + ((Data(1) << 8) Or Data(2))
        '逐个读取根下的具名标签，找 servers 列表
        Dim PosHolder As Integer = Pos
        While PosHolder < Data.Length
            Dim TagType As Byte = Data(PosHolder)
            If TagType = 0 Then Exit While
            PosHolder += 1
            Dim NameLen As Integer = (Data(PosHolder) << 8) Or Data(PosHolder + 1)
            PosHolder += 2
            Dim Name As String = Text.Encoding.UTF8.GetString(Data, PosHolder, NameLen)
            PosHolder += NameLen
            If TagType = 9 AndAlso Name = "servers" Then
                '列表：元素类型(1) + 数量(4, 大端)
                Dim ElementType As Byte = Data(PosHolder)
                PosHolder += 1
                Dim Count As Integer = (Data(PosHolder) << 24) Or (Data(PosHolder + 1) << 16) Or (Data(PosHolder + 2) << 8) Or Data(PosHolder + 3)
                PosHolder += 4
                For i As Integer = 1 To Count
                    '每个元素是 Compound（无名称头），递归读取其中的 name 与 ip 字符串
                    Dim EntryName As String = "", EntryIp As String = ""
                    PosHolder = ReadServerCompound(Data, PosHolder, EntryName, EntryIp)
                    If EntryName <> "" OrElse EntryIp <> "" Then
                        Result.Add(New KeyValuePair(Of String, String)(EntryName, EntryIp))
                    End If
                Next
                Exit While
            Else
                '跳过其他标签
                PosHolder = SkipNbtPayload(Data, PosHolder, TagType)
                If PosHolder < 0 Then Exit While
            End If
        End While
        Return Result
    End Function

    ''' <summary>
    ''' 读取服务器条目 Compound 中的 name 与 ip 字符串字段。
    ''' </summary>
    Private Shared Function ReadServerCompound(Data As Byte(), Pos As Integer, ByRef Name As String, ByRef Ip As String) As Integer
        While Pos < Data.Length
            Dim TagType As Byte = Data(Pos)
            If TagType = 0 Then Return Pos + 1 'Compound 结束
            Pos += 1
            Dim NameLen As Integer = (Data(Pos) << 8) Or Data(Pos + 1)
            Pos += 2
            Dim FieldName As String = Text.Encoding.UTF8.GetString(Data, Pos, NameLen)
            Pos += NameLen
            If TagType = 8 Then
                Dim ValueLen As Integer = (Data(Pos) << 8) Or Data(Pos + 1)
                Pos += 2
                Dim Value As String = Text.Encoding.UTF8.GetString(Data, Pos, ValueLen)
                Pos += ValueLen
                If FieldName = "name" Then Name = Value
                If FieldName = "ip" Then Ip = Value
            Else
                Pos = SkipNbtPayload(Data, Pos, TagType)
                If Pos < 0 Then Return -1
            End If
        End While
        Return Pos
    End Function

    ''' <summary>
    ''' 跳过指定标签的载荷并返回新位置；无法识别的结构返回 -1。
    ''' </summary>
    Private Shared Function SkipNbtPayload(Data As Byte(), Pos As Integer, TagType As Byte) As Integer
        Select Case TagType
            Case 1 : Return Pos + 1 'Byte
            Case 2 : Return Pos + 2 'Short
            Case 3 : Return Pos + 4 'Int
            Case 4 : Return Pos + 8 'Long
            Case 5 : Return Pos + 4 'Float
            Case 6 : Return Pos + 8 'Double
            Case 7 'ByteArray
                Dim Len As Integer = (Data(Pos) << 24) Or (Data(Pos + 1) << 16) Or (Data(Pos + 2) << 8) Or Data(Pos + 3)
                Return Pos + 4 + Len
            Case 8 'String
                Dim Len As Integer = (Data(Pos) << 8) Or Data(Pos + 1)
                Return Pos + 2 + Len
            Case 9 'List
                Dim ElementType As Byte = Data(Pos)
                Dim Count As Integer = (Data(Pos + 1) << 24) Or (Data(Pos + 2) << 16) Or (Data(Pos + 3) << 8) Or Data(Pos + 4)
                Pos += 5
                For i As Integer = 1 To Count
                    Pos = If(ElementType = 10, ReadServerCompound(Data, Pos, Nothing, Nothing), SkipNbtPayload(Data, Pos, ElementType))
                    If Pos < 0 Then Return -1
                Next
                Return Pos
            Case 10 'Compound
                While Pos < Data.Length
                    Dim SubType As Byte = Data(Pos)
                    If SubType = 0 Then Return Pos + 1
                    Pos += 1
                    Dim NameLen As Integer = (Data(Pos) << 8) Or Data(Pos + 1)
                    Pos += 2 + NameLen
                    Pos = SkipNbtPayload(Data, Pos, SubType)
                    If Pos < 0 Then Return -1
                End While
                Return Pos
            Case 11 'IntArray
                Dim Len As Integer = (Data(Pos) << 24) Or (Data(Pos + 1) << 16) Or (Data(Pos + 2) << 8) Or Data(Pos + 3)
                Return Pos + 4 + Len * 4
            Case 12 'LongArray
                Dim Len As Integer = (Data(Pos) << 24) Or (Data(Pos + 1) << 16) Or (Data(Pos + 2) << 8) Or Data(Pos + 3)
                Return Pos + 4 + Len * 8
            Case Else
                Return -1
        End Select
    End Function

    Private Function GetSizeText(Length As Long) As String
        If Length >= 1073741824 Then Return Math.Round(Length / 1073741824, 1) & " GB"
        If Length >= 1048576 Then Return Math.Round(Length / 1048576, 1) & " MB"
        If Length >= 1024 Then Return Math.Round(Length / 1024, 0) & " KB"
        Return Length & " B"
    End Function

    '分类切换与按钮事件
    Private Sub TypeCheck(sender As Object, raiseByMouse As Boolean) Handles BtnTypeSaves.Check, BtnTypeScreenshots.Check, BtnTypeResourcePacks.Check, BtnTypeShaderPacks.Check, BtnTypeSchematics.Check, BtnTypeServers.Check
        If sender.Tag Is Nothing Then Return
        Dim NewType As Integer = Val(sender.Tag)
        If NewType = CurrentType Then Return
        CurrentType = NewType
        Refresh()
    End Sub

    Private Sub BtnOpen_Click(sender As Object, e As EventArgs) Handles BtnOpen.Click
        Try
            Dim Dir As String = CurrentPath()
            If Not Directory.Exists(Dir) Then Directory.CreateDirectory(Dir)
            Process.Start("explorer.exe", $"""{Dir}""")
        Catch ex As Exception
            Logger.Error(ex, "打开文件夹失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Sub BtnRefresh_Click(sender As Object, e As EventArgs) Handles BtnRefresh.Click
        Refresh()
    End Sub

End Class
