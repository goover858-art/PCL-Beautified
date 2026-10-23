Imports System.Globalization
Imports System.IO

''' <summary>
''' 百宝箱的常用工具页面。
''' </summary>
Public Class PageToolsMain

#Region "常用工具"

    ''' <summary>
    ''' 清理垃圾：删除各 MC 版本下的日志、崩溃报告与 Natives，以及启动器临时目录。
    ''' 不会触碰存档、Mod、配置等用户数据。
    ''' </summary>
    Private Sub BtnClear_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnClear.Click
        If ModWatcher.HasRunningMinecraft OrElse McLaunchLoader.State = LoadState.Loading Then
            Hint("请先关闭正在运行的 Minecraft 与启动任务后再清理", HintType.Red)
            Return
        End If
        If MyMsgBox("将删除各个游戏版本下的日志、崩溃报告与 Natives 文件夹，以及启动器的临时目录。" & vbCrLf &
                    "不会影响存档、Mod 与配置文件。是否继续？", "清理垃圾", "开始清理", "取消") <> 1 Then Return
        BtnClear.IsEnabled = False
        RunInNewThread(
        Sub()
            Try
                Dim Count As Integer = 0
                '收集所有可能的 MC 文件夹（全局 + 每个版本独立目录）
                Dim Targets As New List(Of String)
                If McFolderList Is Nothing OrElse Not McFolderList.Any() Then
                    McFolderListLoader.Start()
                    McFolderListLoader.WaitForExit(15000)
                End If
                For Each Folder In McFolderList
                    Dim Base As String = PathUtils.AddSlashSuffix(Folder.Location)
                    Targets.Add(Base)
                    Try
                        Dim VersionsDir As String = Base & "versions\"
                        If Directory.Exists(VersionsDir) Then
                            For Each VersionDir In Directory.GetDirectories(VersionsDir)
                                Targets.Add(PathUtils.AddSlashSuffix(VersionDir))
                            Next
                        End If
                    Catch ex As Exception
                        Logger.Warn(ex, $"枚举版本目录失败：{Base}")
                    End Try
                Next
                '逐个清理
                For Each Base In Targets
                    Count += DeleteFolderSafely(Base & "crash-reports\")
                    Count += DeleteFolderSafely(Base & "logs\")
                    Count += DeleteFolderSafely(Base & "natives-windows-x86_64\")
                    '以 -natives 结尾的版本独立 Natives 目录
                    Try
                        For Each SubDir In Directory.GetDirectories(Base)
                            Dim Name As String = Path.GetFileName(SubDir)
                            If Name.EndsWithF("-natives", True) Then Count += DeleteFolderSafely(PathUtils.AddSlashSuffix(SubDir))
                        Next
                    Catch ex As Exception
                        Logger.Warn(ex, $"枚举 Natives 目录失败：{Base}")
                    End Try
                    '顶层的日志与崩溃转储
                    Count += DeleteFilesSafely(Base)
                Next
                '启动器临时目录
                Count += DeleteFolderSafely(PathTemp)
                DirectoryUtils.Create(PathTemp)
                '结果
                If Count = 0 Then
                    Hint("没有找到需要清理的文件", Log:=False)
                Else
                    Hint($"清理完成，共删除 {Count} 项", Log:=False)
                End If
                Logger.Info($"清理垃圾完成，共删除 {Count} 项")
            Catch ex As Exception
                Logger.Error(ex, "清理垃圾失败")
                Hint("清理垃圾失败：" & ex.Message)
            Finally
                RunInUi(Sub() BtnClear.IsEnabled = True)
            End Try
        End Sub, "Rubbish Clear")
    End Sub

    ''' <summary>
    ''' 递归删除一个文件夹，返回其中删除的条目数。不存在时返回 0。
    ''' </summary>
    Private Function DeleteFolderSafely(FolderPath As String) As Integer
        Try
            If Not Directory.Exists(FolderPath) Then Return 0
            Dim Count As Integer = Directory.GetFileSystemEntries(FolderPath).Length
            Directory.Delete(FolderPath, True)
            Logger.Info($"已删除文件夹：{FolderPath}")
            Return Count
        Catch ex As Exception
            Logger.Warn(ex, $"删除文件夹失败：{FolderPath}")
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' 删除指定目录顶层的日志与崩溃转储文件，返回删除数量。
    ''' </summary>
    Private Function DeleteFilesSafely(FolderPath As String) As Integer
        Dim Count As Integer = 0
        Try
            If Not Directory.Exists(FolderPath) Then Return 0
            For Each FilePath In Directory.GetFiles(FolderPath)
                Dim Name As String = Path.GetFileName(FilePath)
                Dim IsJunk As Boolean =
                    Name.StartsWithF("hs_err_pid", True) OrElse
                    Name.EndsWithF(".log", True) OrElse
                    Name = "WailaErrorOutput.txt"
                If Not IsJunk Then Continue For
                Try
                    File.Delete(FilePath)
                    Count += 1
                Catch ex As Exception
                    Logger.Warn(ex, $"删除文件失败：{FilePath}")
                End Try
            Next
        Catch ex As Exception
            Logger.Warn(ex, $"枚举文件失败：{FolderPath}")
        End Try
        Return Count
    End Function

    ''' <summary>
    ''' 今日人品：以日期 + 启动器 ID 为种子，每天固定一个结果。
    ''' </summary>
    Private Sub BtnLuck_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnLuck.Click
        Try
            Dim Seed As Integer = GenerateDailySeed()
            Dim Random As New Random(Seed)
            Dim Luck As Integer = Random.Next(0, 101)
            Dim Rating As String = GetRating(Luck)
            Dim Title As String = $"今日人品（{Date.Now.ToString("yyyy 年 M 月 d 日", CultureInfo.InvariantCulture)}）"
            If Luck >= 60 Then
                MyMsgBox($"你今天的人品值是：{Luck}" & vbCrLf & Rating, Title)
            Else
                MyMsgBox($"你今天的人品值是：{Luck}" & vbCrLf & Rating, Title, IsWarn:=(Luck <= 30))
            End If
        Catch ex As Exception
            Logger.Error(ex, "计算今日人品失败")
            Hint("计算今日人品失败：" & ex.Message)
        End Try
    End Sub

    Private Shared Function GenerateDailySeed() As Integer
        Dim DatePart As String = Date.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
        Return Djb2Hash(DatePart & Identify)
    End Function

    Private Shared Function Djb2Hash(Text As String) As Integer
        Dim Hash As Long = 5381
        Dim Prime As Long = 33
        For Each C As Char In Text
            Hash = (Hash * Prime + AscW(C)) Mod &H100000000L
        Next
        Return CInt(Hash And &H7FFFFFFFL)
    End Function

    Private Shared Function GetRating(Luck As Integer) As String
        Select Case Luck
            Case 100 : Return "满分！今天做什么都顺，建议抽卡。"
            Case Is >= 95 : Return "极好，运气爆棚的一天。"
            Case Is >= 90 : Return "很好，值得尝试一些冒险。"
            Case Is >= 60 : Return "不错，平平稳稳的一天。"
            Case Is >= 40 : Return "一般，平常心对待即可。"
            Case Is >= 30 : Return "略差，建议低调行事。"
            Case Is >= 10 : Return "较差，尽量别开新坑。"
            Case Else : Return "极差，今天还是老实待着吧。"
        End Select
    End Function

    ''' <summary>
    ''' 在桌面或开始菜单创建启动器快捷方式。
    ''' </summary>
    Private Sub BtnShortcut_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnShortcut.Click
        Try
            Dim ShortcutName As String = "Plain Craft Launcher 2.lnk"
            Dim DesktopPath As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName)
            Dim StartMenuPath As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", ShortcutName)
            Dim Choice As Integer = MyMsgBox(
                "要在哪里创建快捷方式？" & vbCrLf & vbCrLf &
                "桌面：" & DesktopPath & vbCrLf &
                "开始菜单：" & StartMenuPath,
                "创建快捷方式", "取消", "桌面", "开始菜单")
            If Choice = 1 OrElse Choice = 0 Then Return
            Dim Target As String = If(Choice = 2, DesktopPath, StartMenuPath)
            'COM 创建快捷方式（Option Strict Off，允许后期绑定）
            Dim WshShell = CreateObject("WScript.Shell")
            Dim Link = WshShell.CreateShortcut(Target)
            Link.TargetPath = PathExe
            Link.WorkingDirectory = PathExe.BeforeLast("\")
            Link.IconLocation = PathExe
            Link.Description = "Plain Craft Launcher 2"
            Link.Save()
            Hint($"已在{(If(Choice = 2, "桌面", "开始菜单"))}创建快捷方式", Log:=False)
        Catch ex As Exception
            Logger.Error(ex, "创建快捷方式失败")
            Hint("创建快捷方式失败：" & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' 显示启动器累计启动次数。
    ''' </summary>
    Private Sub BtnLaunchCount_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnLaunchCount.Click
        Try
            MyMsgBox($"启动器已经累计启动了 {Settings.Get(Of Integer)("SystemLaunchCount")} 次。" & vbCrLf &
                     "感谢你的每一次使用！", "启动次数")
        Catch ex As Exception
            Logger.Error(ex, "读取启动次数失败")
        End Try
    End Sub

#End Region

#Region "快捷入口"

    Private Sub BtnOpenLog_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenLog.Click
        OpenExplorer(GetPclFolder())
    End Sub

    Private Sub BtnOpenFolder_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenFolder.Click
        OpenExplorer(GetPclFolder())
    End Sub

    Private Sub BtnOpenMcFolder_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenMcFolder.Click
        OpenExplorer(McFolderSelected)
    End Sub

    Private Sub BtnOpenVersionFolder_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenVersionFolder.Click
        OpenExplorer(McFolderSelected & "versions\")
    End Sub

    Private Sub BtnOpenTemp_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenTemp.Click
        OpenExplorer(PathTemp)
    End Sub

    ''' <summary>
    ''' 启动器自身的数据文件夹（exe 同级的 PCL 目录）。
    ''' </summary>
    Private Function GetPclFolder() As String
        Return PathExe.BeforeLast("\") & "\PCL\"
    End Function

#End Region

#Region "自定义下载"

    ''' <summary>
    ''' 选择保存文件夹。
    ''' </summary>
    Private Sub BtnSelectFolder_Click(sender As Object, e As EventArgs) Handles BtnSelectFolder.Click
        Try
            Dim Folder As String = Dialogs.SelectFolder("选择保存位置", False, TextDownloadFolder.Text).FirstOrDefault()
            If String.IsNullOrWhiteSpace(Folder) Then Return
            TextDownloadFolder.Text = Folder
        Catch ex As Exception
            Logger.Error(ex, "选择文件夹失败")
        End Try
    End Sub

    ''' <summary>
    ''' 开始下载。
    ''' </summary>
    Private Sub BtnDownloadStart_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnDownloadStart.Click
        Dim Url As String = If(TextDownloadUrl.Text, "").Trim
        Dim Folder As String = If(TextDownloadFolder.Text, "").Trim
        Dim FileName As String = If(TextDownloadName.Text, "").Trim
        '校验
        If Not (Url.StartsWithF("http://", True) OrElse Url.StartsWithF("https://", True)) Then
            Hint("请输入以 http:// 或 https:// 开头的下载地址", HintType.Red)
            TextDownloadUrl.Focus()
            Return
        End If
        If String.IsNullOrWhiteSpace(Folder) Then
            Hint("请先选择保存位置", HintType.Red)
            Return
        End If
        If String.IsNullOrWhiteSpace(FileName) Then
            FileName = Path.GetFileName(Url.BeforeLast("?").BeforeLast("#"))
            If String.IsNullOrWhiteSpace(FileName) Then FileName = "download"
            TextDownloadName.Text = FileName
        End If
        Try
            DirectoryUtils.Create(Folder)
        Catch ex As Exception
            Logger.Error(ex, $"创建文件夹失败：{Folder}")
            Hint("无法创建保存文件夹：" & ex.Message)
            Return
        End Try
        Dim Target As String = Path.Combine(Folder, FileName)
        If File.Exists(Target) Then
            If MyMsgBox($"文件已存在，是否覆盖？{vbCrLf}{Target}", "文件已存在", "覆盖", "取消", IsWarn:=True) <> 1 Then Return
        End If
        StartCustomDownload(Url, Target)
    End Sub

    ''' <summary>
    ''' 使用启动器自身的多线程下载器下载一个直链文件。
    ''' </summary>
    Private Sub StartCustomDownload(Url As String, Target As String)
        Try
            BtnDownloadStart.IsEnabled = False
            Dim FileTask As New NetFile({Url}, Target, Nothing, True)
            Dim Loader As New LoaderDownload("自定义下载：" & Path.GetFileName(Target) & " ", {FileTask}) With {
                .OnStateChanged = Sub(MyLoader)
                                       RunInUi(Sub() BtnDownloadStart.IsEnabled = MyLoader.State <> LoadState.Loading)
                                   End Sub}
            Loader.Start()
            Hint($"开始下载到 {PathUtils.RemoveLastPart(Target)}", Log:=False)
            Logger.Info($"自定义下载：{Url} -> {Target}")
        Catch ex As Exception
            BtnDownloadStart.IsEnabled = True
            Logger.Error(ex, "启动自定义下载失败")
            Hint("启动下载失败：" & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' 打开当前设置的保存位置。
    ''' </summary>
    Private Sub BtnDownloadOpen_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnDownloadOpen.Click
        Dim Folder As String = If(TextDownloadFolder.Text, "").Trim
        If String.IsNullOrWhiteSpace(Folder) Then
            Hint("还没有选择保存位置", Log:=False)
            Return
        End If
        If Not Directory.Exists(Folder) Then
            Hint("该文件夹不存在：" & Folder, HintType.Red)
            Return
        End If
        OpenExplorer(Folder)
    End Sub

#End Region

#Region "皮肤工具"

    ''' <summary>
    ''' 从 Mojang 获取指定正版玩家当前的皮肤，并另存为 PNG。
    ''' </summary>
    Private Sub BtnSkinFetch_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSkinFetch.Click
        Dim PlayerId As String = If(TextSkinId.Text, "").Trim
        If PlayerId.Length < 2 Then
            Hint("请输入至少 2 个字符的正版玩家 ID", HintType.Red)
            TextSkinId.Focus()
            Return
        End If
        BtnSkinFetch.IsEnabled = False
        Hint($"正在获取 {PlayerId} 的皮肤…", Log:=False)
        RunInNewThread(
        Sub()
            Dim SkinPath As String = ""
            Try
                Dim Uuid As String = McLoginMojangUuid(PlayerId, True)
                Dim Address As String = McSkinGetAddress(Uuid, "Mojang")
                SkinPath = McSkinDownload(Address)
                If String.IsNullOrEmpty(SkinPath) OrElse Not File.Exists(SkinPath) Then Throw New Exception("未能下载到皮肤文件")
                RunInUi(
                Sub()
                    Try
                        Dim Target As String = Dialogs.SaveFile("保存皮肤", PlayerId & ".png", "", {("png", "PNG 图片")})
                        If String.IsNullOrEmpty(Target) Then Return
                        File.Copy(SkinPath, Target, True)
                        Hint($"已保存 {PlayerId} 的皮肤", Log:=False)
                        Logger.Info($"正版皮肤已保存：{PlayerId} -> {Target}")
                    Catch exInner As Exception
                        Logger.Error(exInner, "保存皮肤文件失败")
                        Hint("保存皮肤失败：" & exInner.Message)
                    End Try
                End Sub)
            Catch ex As Exception
                Dim Message As String = ex.Message
                Logger.Warn(ex, $"获取正版皮肤失败（{PlayerId}）")
                RunInUi(
                Sub()
                    If Message.Contains("429") Then
                        Hint("获取皮肤太过频繁，请 5 分钟后再试！", HintType.Red)
                    ElseIf Message.Contains("404") OrElse Message.Contains("未找到") Then
                        Hint($"找不到玩家「{PlayerId}」，请确认 ID 是否正确", HintType.Red)
                    Else
                        Hint("获取皮肤失败：" & Message, HintType.Red)
                    End If
                End Sub)
            Finally
                RunInUi(Sub() BtnSkinFetch.IsEnabled = True)
            End Try
        End Sub, "Skin Fetch")
    End Sub

#End Region

#Region "皮肤头像"

    Private HeadSource As BitmapSource
    Private HeadSkinPath As String

    ''' <summary>
    ''' 当前下拉框选中的头像尺寸。
    ''' </summary>
    Private Function SelectedHeadSize() As Integer
        Try
            Dim Item As MyComboBoxItem = TryCast(CmboHeadSize.SelectedItem, MyComboBoxItem)
            If Item Is Nothing Then Return 64
            Dim Size As Integer = Val(If(Item.Tag, "64").ToString)
            If Size < 8 Then Size = 64
            Return Size
        Catch ex As Exception
            Return 64
        End Try
    End Function

    ''' <summary>
    ''' 选择一个本地皮肤文件并生成头像。
    ''' </summary>
    Private Sub BtnHeadSelect_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnHeadSelect.Click
        Try
            Dim FileName As String = Dialogs.SelectFile("选择皮肤文件", False, filter:={({"png"}, "皮肤文件")}).FirstOrDefault()
            If String.IsNullOrWhiteSpace(FileName) Then Return
            LoadHead(FileName)
        Catch ex As Exception
            Logger.Error(ex, "选择皮肤文件失败")
            Hint("选择皮肤文件失败：" & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' 从皮肤文件生成头像并显示在预览区。
    ''' </summary>
    Private Sub LoadHead(SkinPath As String)
        Try
            HeadSource = SkinHeadRender(SkinPath, SelectedHeadSize())
            HeadSkinPath = SkinPath
            ImgHeadPreview.Source = HeadSource
            PanHeadPreview.Visibility = Visibility.Visible
            BtnHeadSave.IsEnabled = True
            Hint($"头像已生成（{SelectedHeadSize()} × {SelectedHeadSize()}）", Log:=False)
        Catch ex As Exception
            Logger.Error(ex, $"生成头像失败：{SkinPath}")
            Hint("生成头像失败：" & ex.Message)
            PanHeadPreview.Visibility = Visibility.Collapsed
            BtnHeadSave.IsEnabled = False
            HeadSource = Nothing
        End Try
    End Sub

    ''' <summary>
    ''' 切换尺寸后立即重新生成已选择的头像。
    ''' </summary>
    Private Sub CmboHeadSize_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles CmboHeadSize.SelectionChanged
        If String.IsNullOrEmpty(HeadSkinPath) Then Return
        LoadHead(HeadSkinPath)
    End Sub

    ''' <summary>
    ''' 把生成的头像保存为 PNG。
    ''' </summary>
    Private Sub BtnHeadSave_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnHeadSave.Click
        If HeadSource Is Nothing Then
            Hint("请先生成头像", HintType.Red)
            Return
        End If
        Try
            Dim DefaultName As String = If(String.IsNullOrEmpty(HeadSkinPath), "Head", Path.GetFileNameWithoutExtension(HeadSkinPath)) & "-Head.png"
            Dim Target As String = Dialogs.SaveFile("保存头像", DefaultName, "", {("png", "PNG 图片")})
            If String.IsNullOrEmpty(Target) Then Return
            Dim Encoder As New PngBitmapEncoder()
            Encoder.Frames.Add(BitmapFrame.Create(HeadSource))
            Using Stream As New FileStream(Target, FileMode.Create, FileAccess.Write)
                Encoder.Save(Stream)
            End Using
            Hint("头像已保存", Log:=False)
            Logger.Info($"头像已保存：{Target}")
        Catch ex As Exception
            Logger.Error(ex, "保存头像失败")
            Hint("保存头像失败：" & ex.Message)
        End Try
    End Sub

#End Region

#Region "成就生成器"

    Private AchievementSource As BitmapSource

    ''' <summary>
    ''' 按当前填写的内容生成成就卡片预览。
    ''' </summary>
    Private Sub BtnAchPreview_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnAchPreview.Click
        Dim ItemId As String = If(TextAchItem.Text, "").Trim
        Dim Title As String = If(TextAchTitle.Text, "").Trim
        Dim Line1 As String = If(TextAchLine1.Text, "").Trim
        Dim Line2 As String = If(TextAchLine2.Text, "").Trim
        If Title.Length = 0 AndAlso Line1.Length = 0 AndAlso Line2.Length = 0 Then
            Hint("请至少填写成就名称", HintType.Red)
            TextAchTitle.Focus()
            Return
        End If
        BtnAchPreview.IsEnabled = False
        RunInNewThread(
        Sub()
            Try
                Dim Icon As System.Drawing.Bitmap = Nothing
                Dim IconMissing As Boolean = False
                If ItemId.Length > 0 Then
                    Try
                        Dim IconPath As String = McItemIconGet(ItemId)
                        If String.IsNullOrEmpty(IconPath) Then
                            IconMissing = True
                        Else
                            Icon = New System.Drawing.Bitmap(IconPath)
                        End If
                    Catch exIcon As Exception
                        Logger.Warn(exIcon, $"加载成就图标失败：{ItemId}")
                        IconMissing = True
                    End Try
                End If
                Dim Source As BitmapSource = AchievementRender(Icon, Title, Line1, Line2)
                If Icon IsNot Nothing Then Icon.Dispose()
                RunInUi(
                Sub()
                    AchievementSource = Source
                    ImgAchPreview.Source = Source
                    PanAchPreview.Visibility = Visibility.Visible
                    BtnAchSave.IsEnabled = True
                    If IconMissing Then
                        Hint($"找不到物品「{ItemId}」，已生成无图标的预览", HintType.Red)
                    Else
                        Hint("成就预览已生成", Log:=False)
                    End If
                End Sub)
            Catch ex As Exception
                Logger.Error(ex, "生成成就预览失败")
                RunInUi(Sub() Hint("生成成就预览失败：" & ex.Message, HintType.Red))
            Finally
                RunInUi(Sub() BtnAchPreview.IsEnabled = True)
            End Try
        End Sub, "Achievement Preview")
    End Sub

    ''' <summary>
    ''' 把生成的成就卡片保存为 PNG。
    ''' </summary>
    Private Sub BtnAchSave_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnAchSave.Click
        If AchievementSource Is Nothing Then
            Hint("请先生成预览", HintType.Red)
            Return
        End If
        Try
            Dim DefaultName As String = If(TextAchTitle.Text, "").Trim
            If DefaultName.Length = 0 Then DefaultName = "Achievement"
            DefaultName &= ".png"
            Dim Target As String = Dialogs.SaveFile("保存成就图片", DefaultName, "", {("png", "PNG 图片")})
            If String.IsNullOrEmpty(Target) Then Return
            Dim Encoder As New PngBitmapEncoder()
            Encoder.Frames.Add(BitmapFrame.Create(AchievementSource))
            Using Stream As New FileStream(Target, FileMode.Create, FileAccess.Write)
                Encoder.Save(Stream)
            End Using
            Hint("成就图片已保存", Log:=False)
            Logger.Info($"成就图片已保存：{Target}")
        Catch ex As Exception
            Logger.Error(ex, "保存成就图片失败")
            Hint("保存成就图片失败：" & ex.Message)
        End Try
    End Sub

#End Region

#Region "服务器信息查询"

    ''' <summary>
    ''' 查询服务器状态并展示结果。
    ''' </summary>
    Private Sub BtnServerQuery_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnServerQuery.Click
        Dim Host As String = If(TextServerHost.Text, "").Trim
        Dim Port As Integer
        If Not Integer.TryParse(If(TextServerPort.Text, "").Trim, Port) Then Port = 25565
        '允许直接粘贴 host:port 或 host:port 形式的地址
        If Host.Contains(":") Then
            Dim Parts As String() = Host.Split(":"c)
            Host = Parts(0).Trim
            If Parts.Length >= 2 Then
                Dim Parsed As Integer
                If Integer.TryParse(Parts(1).Trim, Parsed) Then Port = Parsed
            End If
            TextServerHost.Text = Host
            TextServerPort.Text = Port.ToString
        End If
        If Host.Length = 0 Then
            Hint("请输入服务器地址", HintType.Red)
            TextServerHost.Focus()
            Return
        End If
        If Port < 1 OrElse Port > 65535 Then
            Hint("端口必须是 1 ~ 65535 之间的整数", HintType.Red)
            TextServerPort.Focus()
            Return
        End If
        BtnServerQuery.IsEnabled = False
        PanServerResult.Visibility = Visibility.Collapsed
        RunInNewThread(
        Sub()
            Try
                Dim Result As ServerPingResult = ServerPing(Host, Port, 8000)
                RunInUi(Sub() ShowServerResult(Result))
            Catch ex As Exception
                Logger.Warn(ex, $"查询服务器失败（{Host}:{Port}）")
                RunInUi(Sub() Hint("查询失败：" & ex.Message, HintType.Red))
            Finally
                RunInUi(Sub() BtnServerQuery.IsEnabled = True)
            End Try
        End Sub, "Server Query")
    End Sub

    ''' <summary>
    ''' 把查询结果渲染到结果区：MOTD 按原版颜色分段着色。
    ''' </summary>
    Private Sub ShowServerResult(Result As ServerPingResult)
        Try
            'MOTD
            LabServerMotd.Inlines.Clear()
            Dim Segments As List(Of MotdSegment) = MotdToSegments(Result.MotdRaw)
            If Segments.Count = 0 Then
                LabServerMotd.Inlines.Add(New Run("（该服务器没有返回 MOTD）"))
            Else
                For Each Segment In Segments
                    Dim Brush As New System.Windows.Media.SolidColorBrush(ColorFromHex(Segment.Color))
                    Brush.Freeze()
                    Dim TextRun As New Run(Segment.Text) With {.Foreground = Brush}
                    If Segment.Bold Then TextRun.FontWeight = FontWeights.Bold
                    LabServerMotd.Inlines.Add(TextRun)
                Next
            End If

            '在线玩家样本
            If Result.SamplePlayers.Any() Then
                Dim Shown As IEnumerable(Of String) = Result.SamplePlayers.Take(10)
                LabServerSample.Text = "在线玩家：" & Shown.Join("、") &
                    If(Result.SamplePlayers.Count > 10, $" 等 {Result.SamplePlayers.Count} 人", "")
                LabServerSample.Visibility = Visibility.Visible
            Else
                LabServerSample.Visibility = Visibility.Collapsed
            End If

            '版本与人数
            Dim VersionText As String = If(String.IsNullOrEmpty(Result.VersionName), "未知版本", Result.VersionName)
            Dim PlayerText As String = If(Result.Online >= 0, $"{Result.Online}/{Result.Max}", "未知")
            LabServerInfo.Text = $"版本 {VersionText}（协议 {Result.Protocol}）　　在线 {PlayerText}　　延迟 {Result.Latency} ms"

            '服务器图标
            ImgServerIcon.Source = DecodeFavicon(Result.Favicon)
            ImgServerIcon.Visibility = If(ImgServerIcon.Source Is Nothing, Visibility.Collapsed, Visibility.Visible)

            PanServerResult.Visibility = Visibility.Visible
            Logger.Info($"服务器查询成功：{Result.VersionName}，{PlayerText}，{Result.Latency}ms")
        Catch ex As Exception
            Logger.Error(ex, "渲染服务器查询结果失败")
            Hint("渲染查询结果失败：" & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' 把 #RRGGBB 转为 WPF 颜色，非法值退回灰色。
    ''' </summary>
    Private Function ColorFromHex(Hex As String) As System.Windows.Media.Color
        Try
            Return System.Windows.Media.ColorConverter.ConvertFromString(If(Hex, "#AAAAAA"))
        Catch ex As Exception
            Return System.Windows.Media.Colors.Gray
        End Try
    End Function

    ''' <summary>
    ''' 解析服务器返回的 data:image/png;base64 图标。
    ''' </summary>
    Private Function DecodeFavicon(DataUri As String) As BitmapSource
        If String.IsNullOrEmpty(DataUri) Then Return Nothing
        Try
            Dim Base64 As String = DataUri.Substring(DataUri.IndexOf(","c) + 1)
            Dim Bytes As Byte() = Convert.FromBase64String(Base64)
            Using Stream As New MemoryStream(Bytes)
                Dim Image As New BitmapImage()
                Image.BeginInit()
                Image.CacheOption = BitmapCacheOption.OnLoad
                Image.StreamSource = Stream
                Image.EndInit()
                Image.Freeze()
                Return Image
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "解析服务器图标失败")
            Return Nothing
        End Try
    End Function

#End Region

#Region "启动标志"

    Private Sub ToolsPageLoaded() Handles Me.Loaded
        RefreshSplashLogo()
        RefreshWallpaperHint()
        RefreshPerfButtons()
    End Sub

    ''' <summary>
    ''' 刷新动态壁纸性能档位按钮的高亮状态。
    ''' </summary>
    Private Sub RefreshPerfButtons()
        Try
            Dim Cur As Integer = 0
            Try
                Cur = Settings.Get(Of Integer)("UiBackgroundDynamicFps")
            Catch
            End Try
            SetPerfButtonStyle(BtnPerfHigh, Cur = 0)
            SetPerfButtonStyle(BtnPerfMid, Cur = 24)
            SetPerfButtonStyle(BtnPerfLow, Cur = 12)
        Catch ex As Exception
            Logger.Warn(ex, "刷新动态壁纸性能按钮失败")
        End Try
    End Sub

    Private Sub SetPerfButtonStyle(Btn As MyButton, IsActive As Boolean)
        Try
            If IsActive Then
                Btn.SetResourceReference(Border.BackgroundProperty, "ColorBrushNavActive")
                Btn.PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrush3")
            Else
                Btn.Background = New MyColor(0, 0, 0, 0)
                Btn.PanFore.SetResourceReference(Border.BorderBrushProperty, "ColorBrush1")
            End If
        Catch ex As Exception
            Logger.Warn(ex, "设置性能按钮样式失败")
        End Try
    End Sub

    ''' <summary>
    ''' 切换动态壁纸性能档位。0 = 流畅（不限帧率），24 = 均衡，12 = 省电。
    ''' </summary>
    Private Sub SetWallpaperPerf(Fps As Integer)
        Try
            Settings.Set("UiBackgroundDynamicFps", Fps)
            RefreshPerfButtons()
            FrmMain.UpdateDynamicBackground()
            Hint("动态壁纸性能已更新", Log:=False)
        Catch ex As Exception
            Logger.Error(ex, "设置动态壁纸性能失败", LogBehavior.Toast)
        End Try
    End Sub

    Private Sub BtnPerfHigh_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnPerfHigh.Click
        SetWallpaperPerf(0)
    End Sub

    Private Sub BtnPerfMid_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnPerfMid.Click
        SetWallpaperPerf(24)
    End Sub

    Private Sub BtnPerfLow_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnPerfLow.Click
        SetWallpaperPerf(12)
    End Sub

    Private Sub RefreshWallpaperHint()
        Try
            Dim TargetPath As String = ""
            Try
                TargetPath = Settings.Get(Of String)("UiBackgroundDynamic")
            Catch
            End Try
            If Not String.IsNullOrWhiteSpace(TargetPath) AndAlso File.Exists(TargetPath) Then
                LabWallpaperPath.Text = TargetPath
            Else
                LabWallpaperPath.Text = "当前没有启用动态壁纸"
            End If
        Catch ex As Exception
            Logger.Warn(ex, "刷新动态壁纸提示失败")
        End Try
    End Sub

    Private Sub BtnWallpaperPick_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnWallpaperPick.Click
        Try
            Dim SourcePath As String = Dialogs.SelectFile("选择动态壁纸", False, filter:={({"gif", "mp4", "m4v", "wmv", "avi", "mov"}, "动图或视频")}).FirstOrDefault()
            If String.IsNullOrWhiteSpace(SourcePath) Then Return
            Dim Folder As String = IO.Path.Combine(Paths.BaseThenName, "DynamicBackground")
            '视频文件可能很大，复制放到后台线程，避免界面卡住
            RunInNewThread(
            Sub()
                Try
                    If Not Directory.Exists(Folder) Then Directory.CreateDirectory(Folder)
                    For Each OldFile As String In Directory.GetFiles(Folder)
                        Try
                            File.Delete(OldFile)
                        Catch
                            '旧文件被占用时忽略
                        End Try
                    Next
                    Dim Target As String = IO.Path.Combine(Folder, "Wallpaper" & IO.Path.GetExtension(SourcePath).ToLowerInvariant())
                    File.Copy(SourcePath, Target, True)
                    RunInUi(
                    Sub()
                        Settings.Set("UiBackgroundDynamic", Target)
                        FrmMain.UpdateDynamicBackground()
                        RefreshWallpaperHint()
                        Hint("动态壁纸已应用", Log:=False)
                    End Sub)
                Catch ex As Exception
                    Logger.Error(ex, "复制动态壁纸失败", LogBehavior.Toast)
                End Try
            End Sub, "Copy Dynamic Wallpaper")
        Catch ex As Exception
            Logger.Error(ex, "设置动态壁纸失败", LogBehavior.Toast)
        End Try
    End Sub

    Private Sub BtnWallpaperClear_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnWallpaperClear.Click
        Try
            Settings.Set("UiBackgroundDynamic", "")
            FrmMain.UpdateDynamicBackground()
            RefreshWallpaperHint()
            Hint("已关闭动态壁纸", Log:=False)
        Catch ex As Exception
            Logger.Error(ex, "关闭动态壁纸失败", LogBehavior.Toast)
        End Try
    End Sub

    Private Sub RefreshSplashLogo()
        Try
            Dim CustomPath As String = ""
            Try
                CustomPath = Settings.Get(Of String)("UiLauncherCustomLogo")
            Catch
            End Try
            If Not String.IsNullOrWhiteSpace(CustomPath) AndAlso File.Exists(CustomPath) Then
                Dim Bmp As New BitmapImage()
                Bmp.BeginInit()
                Bmp.CacheOption = BitmapCacheOption.OnLoad
                Bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache
                Bmp.UriSource = New Uri(CustomPath, UriKind.Absolute)
                Bmp.EndInit()
                Bmp.Freeze()
                ImgLogoPreview.Source = Bmp
                ImgLogoPreview.Visibility = Visibility.Visible
                LabLogoEmpty.Visibility = Visibility.Collapsed
                LabSplashLogoPath.Text = CustomPath
            Else
                ImgLogoPreview.Source = Nothing
                ImgLogoPreview.Visibility = Visibility.Collapsed
                LabLogoEmpty.Visibility = Visibility.Visible
                LabSplashLogoPath.Text = "当前使用默认的 PCL 线稿标志"
            End If
        Catch ex As Exception
            Logger.Warn(ex, "刷新启动标志预览失败")
        End Try
    End Sub

    Private Sub BtnSplashLogoPick_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSplashLogoPick.Click
        Try
            Dim SourcePath As String = Dialogs.SelectFile("选择标志图片", False, filter:={({"png", "jpg", "jpeg", "bmp", "ico"}, "图片文件")}).FirstOrDefault()
            If String.IsNullOrWhiteSpace(SourcePath) Then Return
            '复制到 PCL 数据目录，避免用户之后移动或删除原图导致标志丢失
            Dim Folder As String = IO.Path.Combine(Paths.BaseThenName, "CustomSplash")
            If Not Directory.Exists(Folder) Then Directory.CreateDirectory(Folder)
            For Each OldFile As String In Directory.GetFiles(Folder, "Logo.*")
                Try
                    File.Delete(OldFile)
                Catch
                    '旧文件被占用时忽略，覆盖写入时会处理
                End Try
            Next
            Dim Target As String = IO.Path.Combine(Folder, "Logo" & IO.Path.GetExtension(SourcePath).ToLowerInvariant())
            File.Copy(SourcePath, Target, True)
            Settings.Set("UiLauncherCustomLogo", Target)
            '同一个标志也用于标题栏左上角的 PCL 标
            Try
                Dim TitleTarget As String = Paths.Base & "PCL\Logo.png"
                Dim LogoFolder As String = IO.Path.GetDirectoryName(TitleTarget)
                If Not String.IsNullOrWhiteSpace(LogoFolder) AndAlso Not Directory.Exists(LogoFolder) Then Directory.CreateDirectory(LogoFolder)
                File.Copy(SourcePath, TitleTarget, True)
                Settings.Set("UiLogoType", 3)
                FrmMain.UpdateBackgroundAndTitleBar()
            Catch ex As Exception
                Logger.Warn(ex, "同步标题栏标志失败")
            End Try
            RefreshSplashLogo()
            Hint("标志已更换，标题栏与启动动画同时生效", Log:=False)
        Catch ex As Exception
            Logger.Error(ex, "设置启动标志失败", LogBehavior.Toast)
        End Try
    End Sub

    Private Sub BtnSplashLogoReset_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSplashLogoReset.Click
        Try
            Settings.Set("UiLauncherCustomLogo", "")
            '标题栏恢复成默认的自绘徽标
            Settings.Set("UiLogoType", 1)
            '把标题栏的标志文件改成备份，避免它又被自动识别成自定义标志
            Try
                Dim NativeLogo As String = Paths.Base & "PCL\Logo.png"
                Dim Backup As String = NativeLogo & ".bak"
                If File.Exists(NativeLogo) Then
                    If File.Exists(Backup) Then File.Delete(Backup)
                    File.Move(NativeLogo, Backup)
                End If
            Catch ex As Exception
                Logger.Warn(ex, "备份原标志文件失败")
            End Try
            FrmMain.UpdateBackgroundAndTitleBar()
            RefreshSplashLogo()
            Hint("已恢复默认标志", Log:=False)
        Catch ex As Exception
            Logger.Error(ex, "恢复默认启动标志失败", LogBehavior.Toast)
        End Try
    End Sub

#End Region

End Class
