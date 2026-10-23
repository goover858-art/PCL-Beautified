Imports PCL

''' <summary>
''' 联机页面。
''' 使用 Terracotta 联机模块（与 HMCL 相同的实现方式）：启动独立进程，通过本地 HTTP API 控制房间。
''' </summary>
Public Class PageLinkMain

    '===============================
    '  状态机与前端页面
    '===============================

#Region "状态管理"

    Public Enum LinkStates
        Waiting
        Loading
        Failed
        Finished
    End Enum
    Public Shared LinkState As LinkStates = LinkStates.Waiting

    ''' <summary>
    ''' 切换到指定的状态。
    ''' </summary>
    Private Sub ChangeState(NewState As LinkStates)
        If LinkState = NewState Then Return
        Dim OldState As LinkStates = LinkState
        LinkState = NewState
        Logger.Info($"主状态由 {OldState} 变更为 {NewState}")
        RunInUi(
        Sub()
            Select Case NewState
                Case LinkStates.Waiting
                    SwitchToWaiting(OldState)
                Case LinkStates.Loading
                    SwitchToLoading(OldState)
                Case LinkStates.Failed
                    SwitchToFailed(OldState)
                Case LinkStates.Finished
                    SwitchToFinished(OldState)
            End Select
        End Sub)
    End Sub

    Private Sub LoaderInit() Handles Me.Initialized
        PageLoaderInit(Load, PanLoad, PanContent, Nothing, LinkLoader, AutoRun:=False)
        AddHandler LinkLoader.OnStateChangedUi,
        Sub(Loader As LoaderBase, NewState As LoadState, OldState As LoadState)
            Select Case NewState
                Case LoadState.Finished
                    ChangeState(LinkStates.Finished)
                Case LoadState.Failed
                    ChangeState(LinkStates.Failed)
            End Select
        End Sub
        AddHandler LinkLoader.ProgressChanged,
        Sub(NewProgress As Double, OldProgress As Double)
            RunInUi(Sub() UpdateProgressBar(NewProgress))
        End Sub
        AddHandler ModLink.StateUpdated, AddressOf OnLinkStateUpdated
    End Sub

    '页面动画兼容
    Private Sub UpdatePanelVisibility() Handles Me.PageEnter
        FrmLinkMain.PanSelect.Visibility = If(LinkState = LinkStates.Waiting, Visibility.Visible, Visibility.Collapsed)
        FrmLinkMain.PanFinish.Visibility = If(LinkState = LinkStates.Finished, Visibility.Visible, Visibility.Collapsed)
    End Sub

#End Region

#Region "初始页面 (Waiting)"

    '由任意状态切换到 Waiting
    Private Sub SwitchToWaiting(OldState As LinkStates)
        '加载器与进程状态
        LinkLoader.Cancel()
        SyncLock LinkLoader.LockState
            LinkLoader.State = LoadState.Waiting
        End SyncLock
        ProcessStop()
        '页面切换
        If OldState <> LinkStates.Finished Then
            UpdatePanelVisibility()
        End If
        PageOnContentExit()
    End Sub

    '===============================
    '  页面 UI
    '===============================

    '创建房间
    Private Sub Create_MouseLeftButtonUp() Handles PanSelectCreate.MouseLeftButtonUp
        IsServerSide = True
        JoinCode = Nothing
        RoomCode = Nothing
        Hint("请确保已经在游戏里选择了「对局域网开放」，否则将无法创建房间！", HintType.Blue)
        Logger.Info("尝试创建联机房间")
        ChangeState(LinkStates.Loading)
    End Sub

    '加入房间
    Private Shared LastCode As String = Nothing
    Public Shared Sub Join() Handles PanSelectJoin.MouseLeftButtonUp
        Dim Code As String = MyMsgBoxInput("输入房间号", "输入房主发给你的房间号。",
            HintText:=If(String.IsNullOrEmpty(LastCode), "", "使用上一次的房间号"))
        If Not String.IsNullOrEmpty(LastCode) AndAlso Code IsNot Nothing AndAlso Code = "" Then Code = LastCode
        If Code Is Nothing Then Return
        Join(Code)
    End Sub
    Private Sub JoinInternal(Code As String)
        If Code Is Nothing Then Return
        '基础格式校验
        Dim ValidateResult As String = ValidateCodeFormat(Code)
        If ValidateResult IsNot Nothing Then
            Hint(ValidateResult, HintType.Red)
            Return
        End If
        Code = FixCodeFormat(Code)
        Logger.Info($"实际使用的房间号：{Code}")
        '基础信息
        IsServerSide = False
        JoinCode = Code
        RoomCode = Code
        LastCode = Code
        ChangeState(LinkStates.Loading)
    End Sub
    Public Shared Function ValidateCodeFormat(Code As String) As String
        If Code Is Nothing Then Return "房间号为空！"
        Code = FixCodeFormat(Code)
        If Code.Length = 0 Then Return "房间号为空！"
        If Code.Length > 32 Then Return "房间号有误，请核对后重试！"
        Return Nothing
    End Function
    Private Shared Function FixCodeFormat(Code As String) As String
        If Code Is Nothing Then Return ""
        Code = Code.Between("【", "】").Between("[", "]") '从完整消息中提取
        Return Code.Trim()
    End Function

    '自动加入
    ''' <summary>
    ''' 切换到联机页并立即加入指定房间。
    ''' </summary>
    Public Shared Sub Join(Code As String)
        If LinkState <> LinkStates.Waiting Then
            Hint("你已经在联机房间中了！", HintType.Red)
        ElseIf FrmMain.PageCurrent = FormMain.PageType.Link Then
            FrmLinkMain.JoinInternal(Code)
        Else
            AutoJoinCode = Code
            FrmMain.PageChange(FormMain.PageType.Link, FormMain.PageSubType.LinkMain)
        End If
    End Sub
    '在进入页面时自动尝试加入房间
    Private Shared AutoJoinCode As String = Nothing
    Private Sub PanSelectJoin_Loaded() Handles PanSelectJoin.Loaded
        If AutoJoinCode Is Nothing Then Return
        If LinkState = LinkStates.Waiting Then JoinInternal(AutoJoinCode)
        AutoJoinCode = Nothing
    End Sub

#End Region

#Region "加载页面 (Loading | Failed)"

    '由 Waiting 或 Failed 状态切换到 Loading
    Private Sub SwitchToLoading(OldState As LinkStates)
        LinkLoader.Start(IsForceRestart:=True)
        UpdateProgressBar(0)
        RunInUi(
        Sub()
            LabLoadTitle.Text = If(IsServerSide, "创建房间中", "加入房间中")
        End Sub)
        UpdateLoadingPage("正在准备联机模块……", "准备联机模块")
    End Sub

    '由 Loading 状态切换到 Failed
    Private Sub SwitchToFailed(OldState As LinkStates)
        LinkLoader.Cancel()
        ModLink.StopWatcher()
        ModLink.SetWaiting()
        'UI 更新
        UpdateProgressBar(1)
        RunInUi(
        Sub()
            LabLoadTitle.Text = FailReason
            Dim Brief As String = LinkLoader.Error.GetDisplay(False)
            Dim InnerEx As Exception = LinkLoader.Error
            If InnerEx.Message.StartsWithF("$") Then Brief = InnerEx.Message
            Do Until InnerEx.InnerException Is Nothing
                InnerEx = InnerEx.InnerException
                If InnerEx.Message.StartsWithF("$") Then Brief = InnerEx.Message
            Loop
            LabLoadDesc.Text = If(Brief.StartsWithF("$"), Brief.TrimStart("$"), LinkLoader.Error.GetDisplay(True))
        End Sub)
        Logger.Warn(LinkLoader.Error, FailReason)
    End Sub

    '===============================
    '  页面 UI
    '===============================

    Private Sub UpdateLoadingPage(Title As String, FailBrief As String)
        If FailReason = FailBrief & "失败" Then Return
        FailReason = FailBrief & "失败"
        Logger.Info($"开始步骤：{Title}")
        RunInUiWait(
        Sub()
            If FrmLinkMain Is Nothing OrElse Not FrmLinkMain.LabLoadDesc.IsLoaded Then Return
            FrmLinkMain.LabLoadDesc.Text = Title
        End Sub)
    End Sub
    Private FailReason As String = "准备初始化"

    '取消加载
    Private Sub BtnLoadCancel_Click() Handles BtnLoadCancel.Click
        ChangeState(LinkStates.Waiting)
    End Sub

    '点击重试
    Private Sub CardLoad_MouseLeftButtonUp() Handles CardLoad.MouseLeftButtonUp
        If LinkState <> LinkStates.Failed Then Return
        ChangeState(LinkStates.Loading)
    End Sub

    '进度条
    Private Sub UpdateProgressBar(Optional Value As Double = -1)
        If Value = -1 Then Value = LinkLoader.Progress
        Dim DisplayingProgress As Double = ColumnProgressA.Width.Value
        If Math.Round(Value - DisplayingProgress, 3) = 0 Then Return
        If DisplayingProgress <= Value Then
            Dim NewProgress As Double = If(Value = 1, 1, DisplayingProgress +
                (Value - DisplayingProgress) ^ 2 * 0.5)
            AniStart({
                AaGridLengthWidth(ColumnProgressA, NewProgress - ColumnProgressA.Width.Value, 200),
                AaGridLengthWidth(ColumnProgressB, (1 - NewProgress) - ColumnProgressB.Width.Value, 200)
            }, "Link Progress")
        Else
            ColumnProgressA.Width = New GridLength(Value, GridUnitType.Star)
            ColumnProgressB.Width = New GridLength(1 - Value, GridUnitType.Star)
            AniStop("Link Progress")
        End If
    End Sub
    Private Sub CardLoad_SizeChanged() Handles CardLoad.SizeChanged
        RectProgressClip.Rect = New Rect(0, 0, CardLoad.ActualWidth, 12)
    End Sub

#End Region

#Region "完成页面 (Finished)"

    '由 Loading 状态切换到 Finished
    Private Sub SwitchToFinished(OldState As LinkStates)
        UpdatePanelVisibility() '页面的实际切换会由 Loader 调用 MyPageRight 来触发

        'UI 更新
        If IsServerSide Then
            LabFinishTitle.Text = "已创建房间"
            LabFinishDesc.Text = $"把房间号发给朋友，让他们加入你的世界吧！{vbCrLf}房间号：{RoomCode}"
            BtnFinishExit.Text = "关闭"
            BtnFinishCopy.Visibility = Visibility.Visible
            Copy() '立即复制房间号
        Else
            LabFinishTitle.Text = "已加入房间"
            LabFinishDesc.Text = $"回到游戏，在多人游戏页面的最下方就能找到联机房间！{vbCrLf}注意：使用离线登录时不要手动输入 IP！"
            BtnFinishExit.Text = "离开"
            BtnFinishCopy.Visibility = Visibility.Collapsed
        End If
        RefreshPeople()
        ModLink.StartWatcher()
    End Sub

    '===============================
    '  页面 UI
    '===============================

    ''' <summary>
    ''' 刷新房间内人数。
    ''' </summary>
    Private Sub RefreshPeople()
        Dim Snapshot As ModLink.LinkStatus = ModLink.State
        Dim Names As List(Of String) = Snapshot.Profiles
        Dim Count As Integer = Math.Max(Names.Count, 1)
        RunInUi(
        Sub()
            If FrmLinkMain Is Nothing Then Return
            FrmLinkMain.LabFinishPlayer.Text = $"{Count} 人"
            FrmLinkMain.BtnFinishPlayer.ToolTip = If(Names.Any(), "房间内玩家：" & vbCrLf & Names.Join(vbCrLf), "房间内人数")
        End Sub)
    End Sub

    '后台状态变化
    Private Sub OnLinkStateUpdated(NewState As ModLink.LinkStatus)
        Select Case NewState.Kind
            Case ModLink.LinkStatusKind.HostOK, ModLink.LinkStatusKind.GuestOK
                If NewState.RoomCode <> "" Then RoomCode = NewState.RoomCode
                If LinkState = LinkStates.Finished Then
                    RefreshPeople()
                ElseIf LinkState = LinkStates.Loading Then
                    '由加载线程负责切换页面
                End If
            Case ModLink.LinkStatusKind.ExceptionState, ModLink.LinkStatusKind.Fatal
                If LinkState = LinkStates.Finished Then
                    Dim Text As String = ModLink.GetExceptionText(NewState)
                    If Text Is Nothing Then Text = "$联机已断开。"
                    Hint(Text.TrimStart("$"), HintType.Red)
                    ChangeState(LinkStates.Waiting)
                End If
        End Select
    End Sub

    '退出
    Private Sub BtnFinishExit_Click() Handles BtnFinishExit.Click
        TryExit(False, False)
    End Sub
    ''' <summary>
    ''' 退出联机。
    ''' 返回是否弹出了警告窗口并且玩家选择了取消。
    ''' </summary>
    Public Function TryExit(Silent As Boolean, Closing As Boolean) As Boolean
        If LinkState = LinkStates.Waiting OrElse LinkState = LinkStates.Failed Then Return False
        If Not Silent Then
            If IsServerSide AndAlso ModLink.State.Profiles.Count > 1 Then
                If MyMsgBox("你确定要关闭联机房间吗？" & vbCrLf & "所有玩家都需要重新输入房间号才可加入游戏！", "退出联机", "确定", "取消", IsWarn:=True) = 2 Then Return True
            ElseIf Closing Then
                If MyMsgBox(If(IsServerSide, "你确定要关闭联机房间吗？", "你确定要离开联机房间吗？"), "退出联机", "确定", "取消") = 2 Then Return True
            End If
        End If
        ChangeState(LinkStates.Waiting)
        Return False
    End Function

    '复制房间号
    Private Sub Copy() Handles BtnFinishCopy.Click
        Dim CodeText As String = $"在 PCL 启动器中输入房间号【{RoomCode}】，即可加入联机房间！"
        ClipboardSet(CodeText, False)
        Hint("已复制房间号！", HintType.Green)
    End Sub

#End Region

    '===============================
    '  后端
    '===============================

    ''' <summary>
    ''' 房间号。
    ''' </summary>
    Private Shared RoomCode As String = Nothing
    ''' <summary>
    ''' 加入房间时使用的房间号。
    ''' </summary>
    Private Shared JoinCode As String = Nothing
    ''' <summary>
    ''' 当前是房主还是房客。
    ''' </summary>
    Private Shared IsServerSide As Boolean = False

    Private WithEvents LinkLoader As New LoaderTask(Of Integer, Integer)("联机", AddressOf InitLink) With {.ProgressWeight = 1, .Show = False}

    ''' <summary>
    ''' 联机主流程：准备模块 → 触发 → 等待就绪。
    ''' </summary>
    Private Sub InitLink(Task As LoaderTask(Of Integer, Integer))
        Dim Cancel As CancellationToken = Task.CreateCancellationToken()

        '1. 下载并启动联机模块
        UpdateLoadingPage("正在准备联机模块……", "准备联机模块")
        ModLink.Prepare(Task, Cancel)
        If Task.IsCanceled Then Return
        Task.Progress = 0.95

        '2. 触发联机
        If IsServerSide Then
            UpdateLoadingPage("正在扫描局域网端口……", "扫描端口")
            ModLink.StartScanning()
        Else
            UpdateLoadingPage("正在连接房主……", "连接房间")
            ModLink.StartGuesting(JoinCode)
        End If
        If Task.IsCanceled Then Return

        '3. 等待就绪
        WaitForReady(Task, Cancel)
        Task.Progress = 1
    End Sub

    ''' <summary>
    ''' 轮询联机模块状态，直到房间就绪或出错。
    ''' </summary>
    Private Sub WaitForReady(Task As LoaderTask(Of Integer, Integer), Cancel As CancellationToken)
        '房主需要等待玩家在游戏中开放局域网，因此不设置超时
        Dim Deadline As DateTime = If(IsServerSide, DateTime.MaxValue, Now.AddSeconds(120))
        Dim LastKind As ModLink.LinkStatusKind = ModLink.LinkStatusKind.Unknown
        Do
            If Task.IsCanceled Then Return
            Thread.Sleep(200)
            Dim Snapshot As ModLink.LinkStatus = Nothing
            Try
                Snapshot = ModLink.ReadState(ModLink.GetLinkPort(), 8000)
            Catch ex As Exception
                ex.ThrowIfCanceled()
                If Now > Deadline Then Throw New Exception("$无法与联机模块通信，请重试。", ex)
                Continue Do
            End Try
            '更新提示文本
            If Snapshot.Kind <> LastKind Then
                LastKind = Snapshot.Kind
                UpdateLoadingPage(ModLink.GetStateText(Snapshot.Kind) & "……", ModLink.GetStateFailBrief(Snapshot.Kind))
            End If
            '判断结果
            Select Case Snapshot.Kind
                Case ModLink.LinkStatusKind.HostOK, ModLink.LinkStatusKind.GuestOK
                    If Snapshot.RoomCode <> "" Then RoomCode = Snapshot.RoomCode
                    ModLink.PublishState(Snapshot)
                    Return
                Case ModLink.LinkStatusKind.ExceptionState
                    Throw New Exception(ModLink.GetExceptionText(Snapshot))
                Case ModLink.LinkStatusKind.Fatal
                    Throw New Exception(If(Snapshot.RawState, "$联机模块出错，请重试。"))
            End Select
            If Now > Deadline Then Throw New Exception("$联机超时，请检查网络环境后重试。")
        Loop
    End Sub

    ''' <summary>
    ''' 停止联机模块进程。
    ''' </summary>
    Public Shared Sub ProcessStop()
        ModLink.StopWatcher()
        ModLink.StopProcess()
    End Sub

End Class
