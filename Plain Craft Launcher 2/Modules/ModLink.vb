' 联机模块（Terracotta）接入层
' 参考 HMCL 的实现：内置并自动下载 terracotta 可执行文件，通过本地 HTTP API 控制联机状态。
' Terracotta 以 AGPL-3.0 发布并带有 IPC 例外条款；此处仅以独立进程方式调用，未修改其二进制。

Imports System.Security.Cryptography

Public Module ModLink

    '===============================
    '  状态定义
    '===============================

    ''' <summary>
    ''' 联机模块的状态种类。
    ''' </summary>
    Public Enum LinkStatusKind
        ''' <summary>尚未初始化。</summary>
        Bootstrap
        ''' <summary>联机模块未安装。</summary>
        Uninitialized
        ''' <summary>正在下载或安装联机模块。</summary>
        Preparing
        ''' <summary>正在启动联机模块。</summary>
        Launching
        ''' <summary>已启动，等待进一步状态。</summary>
        Unknown
        ''' <summary>空闲。</summary>
        Waiting
        ''' <summary>房主：正在扫描局域网端口。</summary>
        HostScanning
        ''' <summary>房主：正在创建房间。</summary>
        HostStarting
        ''' <summary>房主：房间已就绪。</summary>
        HostOK
        ''' <summary>房客：正在连接房主。</summary>
        GuestConnecting
        ''' <summary>房客：正在进入房间。</summary>
        GuestStarting
        ''' <summary>房客：已进入房间。</summary>
        GuestOK
        ''' <summary>联机过程中出现异常。</summary>
        ExceptionState
        ''' <summary>致命错误。</summary>
        Fatal
    End Enum

    ''' <summary>
    ''' 联机模块的一次状态快照。
    ''' </summary>
    Public Class LinkStatus
        Public Kind As LinkStatusKind = LinkStatusKind.Bootstrap
        ''' <summary>状态序号，严格递增。</summary>
        Public Index As Integer = -1
        ''' <summary>本地 HTTP API 端口。</summary>
        Public Port As Integer = -1
        ''' <summary>房间号，仅在 HostOK 时有效。</summary>
        Public RoomCode As String = Nothing
        ''' <summary>房客可用的服务器地址，仅在 GuestOK 时有效。</summary>
        Public Url As String = Nothing
        ''' <summary>连接难度，仅在 GuestStarting 时有效。</summary>
        Public Difficulty As Integer = -1
        ''' <summary>异常种类，仅在 ExceptionState 时有效。</summary>
        Public ExceptionType As Integer = -1
        ''' <summary>致命错误种类。</summary>
        Public FatalType As String = Nothing
        ''' <summary>房间内已连接的玩家名称。</summary>
        Public Profiles As New List(Of String)
        ''' <summary>原始状态名，用于日志。</summary>
        Public RawState As String = Nothing

        ''' <summary>
        ''' 是否已经连接到房间（房主或房客）。
        ''' </summary>
        Public ReadOnly Property IsConnected As Boolean
            Get
                Return Kind = LinkStatusKind.HostOK OrElse Kind = LinkStatusKind.GuestOK
            End Get
        End Property

        ''' <summary>
        ''' 是否处于进行中（联机模块应当保持运行）。
        ''' </summary>
        Public ReadOnly Property IsActive As Boolean
            Get
                Select Case Kind
                    Case LinkStatusKind.Unknown, LinkStatusKind.Waiting, LinkStatusKind.HostScanning, LinkStatusKind.HostStarting,
                         LinkStatusKind.HostOK, LinkStatusKind.GuestConnecting, LinkStatusKind.GuestStarting, LinkStatusKind.GuestOK,
                         LinkStatusKind.ExceptionState
                        Return True
                    Case Else
                        Return False
                End Select
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return $"{Kind}（Index={Index}, Port={Port}）"
        End Function
    End Class

    '===============================
    '  常量
    '===============================

    ''' <summary>
    ''' 联机模块的文件清单，来自 HMCL 的 assets/terracotta.json（仅保留 Windows 平台）。
    ''' 使用单引号书写以便于阅读，加载时统一替换为双引号。
    ''' </summary>
    Private ReadOnly ManifestJson As String = "{
        'version_latest':'0.4.2',
        'packages':{
            'windows-x86_64':{
                'hash':'6a98f524d4f00373696517306af8aa50d01d55ce4eadb27e9e4bc2f882707a0b5f20d5d4c33371d1459dcf5bf144ffed9beb414202d9ccf32b11dbbfcf19d650',
                'files':{
                    'VCRUNTIME140.DLL':'3d4b24061f72c0e957c7b04a0c4098c94c8f1afb4a7e159850b9939c7210d73398be6f27b5ab85073b4e8c999816e7804fef0f6115c39cd061f4aaeb4dcda8cf',
                    'terracotta-0.4.2-windows-x86_64.exe':'6e98d1f2380ed22fb5a2dd4aafce6c773e9cf69100c8bb8e49e7d6983756bdb9a31f80e06bcfbe5a2742144fe806d3d687dec54d8f09d87c659341f99dd9fd80'
                }
            },
            'windows-arm64':{
                'hash':'fc1077247014ac0c712469498bde2ef7f6d881d5fcb7bdd5e11ebe20218fed365be19afdb8d453a79d77b729f866058522b910741767f4df947faa891434b463',
                'files':{
                    'VCRUNTIME140.DLL':'5cb5ce114614101d260f4754c09e8a0dd57e4da885ebb96b91e274326f3e1dd95ed0ade9f542f1922fad0ed025e88a1f368e791e1d01fae69718f0ec3c7b98c8',
                    'terracotta-0.4.2-windows-arm64.exe':'30a15c5c53e5817c5a3634532172559327474741d3b2c7ef4e8a30acc6f59cdcf3570bf5f583e3cbe9e2abc8253e977c1abda1e9f36c88c4e99240da257347d0'
                }
            }
        },
        'downloads':[
            'https://github.com/burningtnt/Terracotta/releases/download/v${version}/terracotta-${version}-${classifier}-pkg.tar.gz'
        ],
        'downloads_CN':[
            'https://gitee.com/burningtnt/Terracotta/releases/download/v${version}/terracotta-${version}-${classifier}-pkg.tar.gz',
            'https://cnb.cool/HMCL-Terracotta/Terracotta/-/releases/download/v${version}/terracotta-${version}-${classifier}-pkg.tar.gz',
            'https://alist.8mi.tech/d/mirror/HMCL-Terracotta/Auto/v${version}/terracotta-${version}-${classifier}-pkg.tar.gz'
        ]
    }".Replace("'"c, """"c)

    ''' <summary>
    ''' 公共节点列表，用于在无法直连时中继。
    ''' </summary>
    Private Const NodeListUrl As String = "https://terracotta.glavo.site/nodes"

    '===============================
    '  路径
    '===============================

    ''' <summary>
    ''' 联机模块所在的文件夹路径，以 \ 结尾。
    ''' </summary>
    Public ReadOnly Property PathLink As String = Paths.BaseThenName & "Terracotta\"

    Private _Manifest As JObject = Nothing
    ''' <summary>
    ''' 解析后的联机模块清单。
    ''' </summary>
    Private ReadOnly Property Manifest As JObject
        Get
            If _Manifest Is Nothing Then _Manifest = JObject.Parse(ManifestJson)
            Return _Manifest
        End Get
    End Property

    ''' <summary>
    ''' 联机模块的最新版本号。
    ''' </summary>
    Public ReadOnly Property LinkVersion As String
        Get
            Return Manifest("version_latest").ToString
        End Get
    End Property

    ''' <summary>
    ''' 当前平台对应的包标识。
    ''' </summary>
    Public ReadOnly Property LinkClassifier As String
        Get
            Return If(IsArm64(), "windows-arm64", "windows-x86_64")
        End Get
    End Property

    Private Function IsArm64() As Boolean
        Try
            Select Case GetType(String).Assembly.GetName().ProcessorArchitecture
                Case Reflection.ProcessorArchitecture.Arm
                    Return True
                Case Else
                    Return False
            End Select
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 联机模块安装目录，以 \ 结尾。
    ''' </summary>
    Public ReadOnly Property PathLinkVersion As String
        Get
            Return PathLink & LinkVersion & "\"
        End Get
    End Property

    ''' <summary>
    ''' 联机模块主程序的完整路径。
    ''' </summary>
    Public ReadOnly Property PathLinkExe As String
        Get
            Return PathLinkVersion & $"terracotta-{LinkVersion}-{LinkClassifier}.exe"
        End Get
    End Property

    ''' <summary>
    ''' 联机模块包内所有文件及其 SHA-512 校验值。
    ''' </summary>
    Private Function GetManifestFiles() As Dictionary(Of String, String)
        Dim Result As New Dictionary(Of String, String)
        Dim Pkg = Manifest("packages")(LinkClassifier)
        If Pkg Is Nothing Then Return Result
        For Each Item As JProperty In CType(Pkg("files"), JObject).Properties
            Result(Item.Name) = Item.Value.ToString.ToLower
        Next
        Return Result
    End Function

    '===============================
    '  状态管理
    '===============================

    Private ReadOnly StateLock As New Object
    Private CurrentState As New LinkStatus

    ''' <summary>
    ''' 当前状态快照。
    ''' </summary>
    Public ReadOnly Property State As LinkStatus
        Get
            SyncLock StateLock
                Return CurrentState
            End SyncLock
        End Get
    End Property

    ''' <summary>
    ''' 当状态在后台被更新时触发（例如房间内玩家数量变化、连接中断）。
    ''' </summary>
    Public Event StateUpdated(NewState As LinkStatus)

    ''' <summary>
    ''' 由页面主动发布一次状态。
    ''' </summary>
    Public Sub PublishState(NewState As LinkStatus)
        SyncLock StateLock
            CurrentState = NewState
        End SyncLock
        Logger.Info($"联机状态更新为 {NewState}")
        RunInUi(Sub() RaiseStateUpdated(NewState))
    End Sub

    Private Sub RaiseStateUpdated(NewState As LinkStatus)
        RaiseEvent StateUpdated(NewState)
    End Sub

    Private Sub PublishStateUi(NewState As LinkStatus)
        RunInUi(Sub() RaiseStateUpdated(NewState))
    End Sub

    ''' <summary>
    ''' 重置为未启动状态。
    ''' </summary>
    Public Sub ResetState()
        PublishState(New LinkStatus With {.Kind = LinkStatusKind.Bootstrap})
    End Sub

    ''' <summary>
    ''' 获取状态对应的提示文本。
    ''' </summary>
    Public Function GetStateText(Kind As LinkStatusKind) As String
        Select Case Kind
            Case LinkStatusKind.Bootstrap, LinkStatusKind.Uninitialized
                Return "正在准备联机模块"
            Case LinkStatusKind.Preparing
                Return "正在下载联机模块"
            Case LinkStatusKind.Launching
                Return "正在启动联机模块"
            Case LinkStatusKind.Unknown, LinkStatusKind.Waiting
                Return "正在等待联机模块响应"
            Case LinkStatusKind.HostScanning
                Return "正在扫描局域网端口"
            Case LinkStatusKind.HostStarting
                Return "正在创建房间"
            Case LinkStatusKind.HostOK
                Return "房间已创建"
            Case LinkStatusKind.GuestConnecting
                Return "正在连接房主"
            Case LinkStatusKind.GuestStarting
                Return "正在进入房间"
            Case LinkStatusKind.GuestOK
                Return "已加入房间"
            Case LinkStatusKind.ExceptionState
                Return "联机出现异常"
            Case Else
                Return "联机模块出错"
        End Select
    End Function

    ''' <summary>
    ''' 获取状态对应的失败简述。
    ''' </summary>
    Public Function GetStateFailBrief(Kind As LinkStatusKind) As String
        Select Case Kind
            Case LinkStatusKind.HostScanning
                Return "扫描端口失败"
            Case LinkStatusKind.HostStarting
                Return "创建房间失败"
            Case LinkStatusKind.GuestConnecting
                Return "连接房主失败"
            Case LinkStatusKind.GuestStarting
                Return "进入房间失败"
            Case LinkStatusKind.ExceptionState
                Return "联机"
            Case Else
                Return "启动联机模块失败"
        End Select
    End Function

    ''' <summary>
    ''' 获取异常状态对应的说明文本。
    ''' </summary>
    Public Function GetExceptionText(State As LinkStatus) As String
        If State.Kind = LinkStatusKind.Fatal Then Return If(State.RawState, "联机模块发生错误")
        Select Case State.ExceptionType
            Case 0
                Return $"$无法连接到房主。{vbCrLf}请确认房主仍在房间中，并检查双方的网络环境。"
            Case 1
                Return $"$与房主的连接被重置。{vbCrLf}这通常是房主关闭了游戏或房间导致的。"
            Case 2
                Return $"$房客的联机模块崩溃了。{vbCrLf}请尝试重新加入房间。"
            Case 3
                Return $"$房主的联机模块崩溃了。{vbCrLf}请让房主重新创建房间。"
            Case 4
                Return $"$无法连接联机服务器。{vbCrLf}请检查网络连接，或稍后再试。"
            Case 5
                Return $"$联机服务器返回了无效的响应。{vbCrLf}请稍后再试。"
            Case Else
                Return "$联机过程中出现未知异常。"
        End Select
    End Function

    '===============================
    '  安装与校验
    '===============================

    Private InstalledCache As Boolean? = Nothing

    ''' <summary>
    ''' 检查联机模块是否已安装。
    ''' </summary>
    Public Function IsInstalled(Optional ForceRecheck As Boolean = False) As Boolean
        If ForceRecheck Then InstalledCache = Nothing
        If InstalledCache IsNot Nothing Then Return InstalledCache.Value
        Try
            Dim Files = GetManifestFiles()
            If Not Files.Any() Then Return False
            For Each Pair In Files
                Dim Target As String = PathLinkVersion & Pair.Key
                If Not FileUtils.Exists(Target) Then
                    InstalledCache = False
                    Return False
                End If
            Next
            InstalledCache = True
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "检查联机模块安装状态失败")
            InstalledCache = False
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 确保联机模块已安装并启动，返回本地 HTTP API 端口。
    ''' </summary>
    ''' <param name="Loader">用于同步进度的加载器，可以为 Nothing。</param>
    ''' <param name="Cancel">取消令牌。</param>
    Public Function Prepare(Optional Loader As LoaderBase = Nothing, Optional Cancel As CancellationToken = Nothing) As Integer
        '已启动则直接返回端口
        If LinkPort > 0 AndAlso LinkProcess IsNot Nothing AndAlso Not LinkProcess.HasExited Then Return LinkPort
        '安装
        If Not IsInstalled() Then
            PublishState(New LinkStatus With {.Kind = LinkStatusKind.Preparing})
            DownloadAndInstall(Loader, Cancel)
            InstalledCache = True
        End If
        '启动
        PublishState(New LinkStatus With {.Kind = LinkStatusKind.Launching})
        Dim Result As Integer = LaunchProcess(Loader, Cancel)
        '确保联机模块处于空闲状态，避免复用的守护进程残留上一个房间
        Try
            HttpGet($"http://127.0.0.1:{Result}/state/ide", 10000)
        Catch ex As Exception
            Logger.Warn(ex, "重置联机模块状态失败")
        End Try
        Return Result
    End Function

    ''' <summary>
    ''' 下载并安装联机模块。
    ''' </summary>
    Private Sub DownloadAndInstall(Loader As LoaderBase, Cancel As CancellationToken)
        Dim Version As String = LinkVersion
        Dim Classifier As String = LinkClassifier
        Dim Pkg = Manifest("packages")(Classifier)
        If Pkg Is Nothing Then Throw New Exception($"$当前平台暂不支持联机功能（{Classifier}）。")
        Dim Expected As String = Pkg("hash").ToString.ToLower

        '组装下载地址，优先使用国内镜像
        Dim Urls As New List(Of String)
        For Each Key In {"downloads_CN", "downloads"}
            Dim Arr = TryCast(Manifest(Key), JArray)
            If Arr Is Nothing Then Continue For
            For Each Item In Arr
                Dim Url As String = Item.ToString.Replace("${version}", Version).Replace("${classifier}", Classifier)
                If Not Urls.Contains(Url) Then Urls.Add(Url)
            Next
        Next

        '下载压缩包
        Dim TarPath As String = PathTemp & $"terracotta-{Version}-{Classifier}-pkg.tar.gz"
        Dim Reuse As Boolean = False
        If FileUtils.Exists(TarPath) Then
            If String.Equals(CryptographyUtils.ComputeFileHash(TarPath, CryptographyUtils.HashMethod.Sha512), Expected, StringComparison.OrdinalIgnoreCase) Then
                Reuse = True
                Logger.Info("已存在校验通过的联机模块压缩包，跳过下载")
            Else
                FileUtils.Delete(TarPath)
            End If
        End If
        If Not Reuse Then DownloadFile(Urls, TarPath, Expected, Loader, Cancel, 0, 0.85)

        Cancel.ThrowIfCancellationRequested()
        '解压
        If Loader IsNot Nothing Then Loader.Progress = 0.9
        Directory.CreateDirectory(PathLinkVersion)
        ExtractTarGz(TarPath, PathLinkVersion, Cancel)

        '校验文件完整性
        If Loader IsNot Nothing Then Loader.Progress = 0.96
        For Each Pair In GetManifestFiles()
            Dim Target As String = PathLinkVersion & Pair.Key
            If Not FileUtils.Exists(Target) Then
                Throw New Exception($"$联机模块文件缺失（{Pair.Key}），请稍后重试。")
            End If
            Dim Actual As String = CryptographyUtils.ComputeFileHash(Target, CryptographyUtils.HashMethod.Sha512)
            If Not String.Equals(Actual, Pair.Value, StringComparison.OrdinalIgnoreCase) Then
                FileUtils.Delete(Target)
                Throw New Exception($"$联机模块文件校验失败（{Pair.Key}），请稍后重试。")
            End If
        Next

        FileUtils.Delete(TarPath)
        Logger.Info("联机模块安装完成")
    End Sub

    ''' <summary>
    ''' 下载文件，依次尝试候选地址，并校验 SHA-512。
    ''' </summary>
    Private Sub DownloadFile(Urls As List(Of String), LocalPath As String, ExpectedHash As String,
                             Loader As LoaderBase, Cancel As CancellationToken, FromPercentage As Double, ToPercentage As Double)
        Dim LastError As Exception = Nothing
        For Each Url In Urls
            Cancel.ThrowIfCancellationRequested()
            Try
                Logger.Info($"开始下载联机模块：{Url}")
                FileUtils.Delete(LocalPath)
                Dim Request As HttpWebRequest = DirectCast(WebRequest.Create(Url), HttpWebRequest)
                Request.UserAgent = "PCL2/" & VersionCode
                Request.Timeout = 30000
                Request.ReadWriteTimeout = 30000
                Request.AllowAutoRedirect = True
                Request.Proxy = WebRequest.DefaultWebProxy
                Using Response As WebResponse = Request.GetResponse()
                    Dim Total As Long = Response.ContentLength
                    Using Source = Response.GetResponseStream(), Target As New FileStream(LocalPath, FileMode.Create, FileAccess.Write, FileShare.None)
                        Dim Buffer(8191) As Byte
                        Dim ReadTotal As Long = 0
                        Dim LastReport As Double = -1
                        Do
                            Cancel.ThrowIfCancellationRequested()
                            Dim Count As Integer = Source.Read(Buffer, 0, Buffer.Length)
                            If Count <= 0 Then Exit Do
                            Target.Write(Buffer, 0, Count)
                            ReadTotal += Count
                            If Total > 0 Then
                                Dim P As Double = FromPercentage + (ToPercentage - FromPercentage) * ReadTotal / Total
                                If Loader IsNot Nothing AndAlso P - LastReport >= 0.005 Then
                                    Loader.Progress = P
                                    LastReport = P
                                End If
                            End If
                        Loop
                    End Using
                End Using
                '校验
                Dim Actual As String = CryptographyUtils.ComputeFileHash(LocalPath, CryptographyUtils.HashMethod.Sha512)
                If Not String.Equals(Actual, ExpectedHash, StringComparison.OrdinalIgnoreCase) Then
                    FileUtils.Delete(LocalPath)
                    Throw New Exception("下载到的联机模块校验失败")
                End If
                Logger.Info("联机模块下载完成")
                Return
            Catch ex As Exception
                ex.ThrowIfCanceled()
                FileUtils.Delete(LocalPath)
                Logger.Warn(ex, $"下载联机模块失败（{Url}）")
                LastError = ex
            End Try
        Next
        Throw New Exception("$无法下载联机模块，请检查网络连接后重试！", LastError)
    End Sub

    '===============================
    '  tar.gz 解压
    '===============================

    ''' <summary>
    ''' 解压 tar.gz 压缩包。.NET Framework 不提供 tar 支持，此处实现最小可用的解析。
    ''' </summary>
    Private Sub ExtractTarGz(GzPath As String, TargetDir As String, Cancel As CancellationToken)
        Directory.CreateDirectory(TargetDir)
        Dim Header(511) As Byte
        Dim PendingName As String = Nothing
        Using Raw As New IO.FileStream(GzPath, FileMode.Open, FileAccess.Read, FileShare.Read)
            Using Gz As New GZipStream(Raw, CompressionMode.Decompress)
                Do
                    Cancel.ThrowIfCancellationRequested()
                    If Not ReadExact(Gz, Header, 512) Then Exit Do
                    Dim IsEmpty As Boolean = True
                    For i = 0 To 511
                        If Header(i) <> 0 Then
                            IsEmpty = False
                            Exit For
                        End If
                    Next
                    If IsEmpty Then Exit Do '两个全零块表示归档结束

                    Dim Name As String = ReadTarString(Header, 0, 100)
                    Dim Prefix As String = ReadTarString(Header, 345, 155)
                    Dim Size As Long = ReadTarSize(Header)
                    Dim Flag As Byte = Header(156)
                    Dim Padding As Integer = CInt((512 - Size Mod 512) Mod 512)

                    Select Case Flag
                        Case 120, 103 'PAX 扩展头
                            Dim Content As Byte() = ReadBlock(Gz, Size)
                            SkipBytes(Gz, Padding)
                            If Flag = 120 Then
                                For Each Line In Encoding.UTF8.GetString(Content).Split(ChrW(10))
                                    Dim Index As Integer = Line.IndexOf(" path=", StringComparison.Ordinal)
                                    If Line.StartsWith("path=", StringComparison.Ordinal) Then
                                        PendingName = Line.Substring(5)
                                    ElseIf Index >= 0 Then
                                        PendingName = Line.Substring(Index + 6)
                                    End If
                                Next
                            End If
                        Case 76 'GNU 长文件名
                            Dim Content As Byte() = ReadBlock(Gz, Size)
                            SkipBytes(Gz, Padding)
                            PendingName = Encoding.UTF8.GetString(Content).TrimEnd(ChrW(0))
                        Case 53 '目录
                            Dim RelDir As String = SafeCombine(TargetDir, Name)
                            Directory.CreateDirectory(RelDir)
                            SkipBytes(Gz, Size + Padding)
                            PendingName = Nothing
                        Case 0, 48 '普通文件
                            Dim RelFile As String = If(PendingName, If(Prefix <> "", Prefix & "/" & Name, Name))
                            PendingName = Nothing
                            Dim Target As String = SafeCombine(TargetDir, RelFile)
                            Dim Parent As String = IO.Path.GetDirectoryName(Target)
                            If Not String.IsNullOrEmpty(Parent) Then Directory.CreateDirectory(Parent)
                            Using Output As New IO.FileStream(Target, FileMode.Create, FileAccess.Write, FileShare.None)
                                CopyExact(Gz, Output, Size)
                            End Using
                            SkipBytes(Gz, Padding)
                        Case Else '链接等其他类型直接跳过
                            PendingName = Nothing
                            SkipBytes(Gz, Size + Padding)
                    End Select
                Loop
            End Using
        End Using
    End Sub

    Private Function ReadTarString(Buffer As Byte(), Offset As Integer, Length As Integer) As String
        Dim EndIndex As Integer = Offset
        While EndIndex < Offset + Length AndAlso Buffer(EndIndex) <> 0
            EndIndex += 1
        End While
        Return Encoding.UTF8.GetString(Buffer, Offset, EndIndex - Offset).Trim()
    End Function

    Private Function ReadTarSize(Buffer As Byte()) As Long
        'base-256 编码
        If (Buffer(124) And &H80) <> 0 Then
            Dim Value As Long = Buffer(124) And &H7F
            For i = 125 To 135
                Value = (Value << 8) Or Buffer(i)
            Next
            Return Value
        End If
        Dim Text As String = ReadTarString(Buffer, 124, 12)
        If Text = "" Then Return 0
        Return Convert.ToInt64(Text, 8)
    End Function

    Private Function ReadExact(Source As Stream, Buffer As Byte(), Length As Integer) As Boolean
        Dim Offset As Integer = 0
        While Offset < Length
            Dim Count As Integer = Source.Read(Buffer, Offset, Length - Offset)
            If Count <= 0 Then Return False
            Offset += Count
        End While
        Return True
    End Function

    Private Function ReadBlock(Source As Stream, Length As Long) As Byte()
        If Length <= 0 Then Return New Byte() {}
        Dim Buffer(CInt(Length) - 1) As Byte
        If Not ReadExact(Source, Buffer, CInt(Length)) Then Throw New Exception("联机模块压缩包不完整")
        Return Buffer
    End Function

    Private Sub CopyExact(Source As Stream, Target As Stream, Length As Long)
        Dim Buffer(8191) As Byte
        Dim Remaining As Long = Length
        While Remaining > 0
            Dim Count As Integer = Source.Read(Buffer, 0, CInt(Math.Min(CLng(Buffer.Length), Remaining)))
            If Count <= 0 Then Throw New Exception("联机模块压缩包不完整")
            Target.Write(Buffer, 0, Count)
            Remaining -= Count
        End While
    End Sub

    Private Sub SkipBytes(Source As Stream, Length As Long)
        If Length <= 0 Then Return
        Dim Buffer(8191) As Byte
        Dim Remaining As Long = Length
        While Remaining > 0
            Dim Count As Integer = Source.Read(Buffer, 0, CInt(Math.Min(CLng(Buffer.Length), Remaining)))
            If Count <= 0 Then Return
            Remaining -= Count
        End While
    End Sub

    ''' <summary>
    ''' 安全地拼接解压路径，阻止目录穿越。
    ''' </summary>
    Private Function SafeCombine(TargetDir As String, Relative As String) As String
        Dim Rel As String = Relative.Replace("/"c, "\"c).TrimStart("\"c)
        Do While Rel.StartsWith("..\")
            Rel = Rel.Substring(3)
        Loop
        Return IO.Path.Combine(TargetDir, Rel)
    End Function

    '===============================
    '  进程管理
    '===============================

    Private LinkProcess As Process = Nothing
    Private LinkPort As Integer = -1

    ''' <summary>
    ''' 获取本地 HTTP API 的端口，未启动时返回 -1。
    ''' 注意：不能命名为 Port，否则会与 VB 的大小写不敏感规则冲突，影响其他模块中的循环变量。
    ''' </summary>
    Public Function GetLinkPort() As Integer
        Return LinkPort
    End Function

    ''' <summary>
    ''' 启动联机模块进程，并等待它回写端口。
    ''' </summary>
    Private Function LaunchProcess(Loader As LoaderBase, Cancel As CancellationToken) As Integer
        Dim ExePath As String = PathLinkExe
        If Not FileUtils.Exists(ExePath) Then Throw New Exception("$联机模块不存在，请重新进入联机页面以重新下载。")

        Dim PortFile As String = IO.Path.Combine(IO.Path.GetTempPath(), $"PCL-Terracotta-{GetUuid()}.json")
        FileUtils.Delete(PortFile)

        Dim Info As New ProcessStartInfo(ExePath, $"--hmcl ""{PortFile}""") With {
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .WorkingDirectory = PathLinkVersion
        }
        Logger.Info($"启动联机模块：{ExePath} --hmcl ""{PortFile}""")
        LinkProcess = Process.Start(Info)
        AddHandler LinkProcess.OutputDataReceived, Sub(Sender, E) OnProcessLog(E.Data)
        AddHandler LinkProcess.ErrorDataReceived, Sub(Sender, E) OnProcessLog(E.Data)
        LinkProcess.BeginOutputReadLine()
        LinkProcess.BeginErrorReadLine()

        Dim StartTime As DateTime = Now
        Dim ExitTime As DateTime? = Nothing
        Do
            Cancel.ThrowIfCancellationRequested()
            Thread.Sleep(50)
            '端口文件写入后即可读取
            If FileUtils.Exists(PortFile) Then
                Try
                    Dim Json As JObject = FileUtils.ReadAsString(PortFile).DeserializeJson()
                    Dim NewPort As Integer = Json("port").ToObject(Of Integer)
                    If NewPort > 0 Then
                        LinkPort = NewPort
                        FileUtils.Delete(PortFile)
                        Logger.Info($"联机模块已就绪，端口 {NewPort}")
                        PublishState(New LinkStatus With {.Kind = LinkStatusKind.Unknown, .Port = NewPort})
                        Return NewPort
                    End If
                Catch ex As Exception
                    '文件可能尚未写完，稍后重试
                    Logger.Trace($"读取联机模块端口文件失败：{ex.Message}")
                End Try
            End If
            '进程已退出
            If LinkProcess.HasExited Then
                If ExitTime Is Nothing Then
                    ExitTime = Now
                ElseIf (Now - ExitTime.Value).TotalSeconds >= 10 Then
                    Throw New Exception($"$联机模块启动失败（进程已退出，退出码 {LinkProcess.ExitCode}）。{vbCrLf}请检查日志以获取更多信息。")
                End If
            Else
                ExitTime = Nothing
            End If
            If (Now - StartTime).TotalSeconds > 60 Then Throw New Exception("$启动联机模块超时，请检查杀毒软件是否拦截了联机模块。")
            If Loader IsNot Nothing Then Loader.Progress = 0.97
        Loop
    End Function

    Private Sub OnProcessLog(Line As String)
        If String.IsNullOrWhiteSpace(Line) Then Return
        Logger.Info($"[联机] {Line}")
    End Sub

    ''' <summary>
    ''' 关闭联机模块进程。
    ''' 联机模块是单实例守护进程，可能在“副模式”下把请求转交给更早启动的进程，
    ''' 因此除了关闭自己启动的进程外，还会按进程名兜底清理残留，避免留下孤儿进程。
    ''' </summary>
    Public Sub StopProcess()
        Dim Proc As Process = LinkProcess
        If Proc Is Nothing Then
            Logger.Info("联机模块进程未被本程序启动")
        Else
            Try
                Logger.Info($"正在关闭联机模块（PID {Proc.Id}，已退出：{Proc.HasExited}）")
                If Not Proc.HasExited Then
                    Proc.Kill()
                    Proc.WaitForExit(3000)
                    Logger.Info("联机模块已关闭")
                End If
            Catch ex As Exception
                Logger.Warn(ex, "关闭联机模块失败")
            End Try
        End If
        '兜底：清理同名残留进程（仅在本程序确实开过房间时执行）
        If LinkPort > 0 Then
            Try
                Dim Name As String = $"terracotta-{LinkVersion}-{LinkClassifier}"
                For Each Leftover As Process In Process.GetProcessesByName(Name)
                    If Proc IsNot Nothing AndAlso Leftover.Id = Proc.Id Then Continue For
                    Logger.Info($"清理残留的联机模块进程（PID {Leftover.Id}）")
                    Leftover.Kill()
                Next
            Catch ex As Exception
                Logger.Warn(ex, "清理残留的联机模块进程失败")
            End Try
        End If
        LinkProcess = Nothing
        LinkPort = -1
        ResetState()
    End Sub

    '===============================
    '  HTTP API
    '===============================

    Private Function HttpGet(Url As String, Optional Timeout As Integer = 20000) As String
        Dim Request As HttpWebRequest = DirectCast(WebRequest.Create(Url), HttpWebRequest)
        Request.Proxy = Nothing '本地回环地址不走代理
        Request.Timeout = Timeout
        Request.ReadWriteTimeout = Timeout
        Request.UserAgent = "PCL2/" & VersionCode
        Using Response As WebResponse = Request.GetResponse()
            Using Reader As New StreamReader(Response.GetResponseStream, Encoding.UTF8)
                Return Reader.ReadToEnd()
            End Using
        End Using
    End Function

    Private Function BuildQuery(Parameters As List(Of KeyValuePair(Of String, String))) As String
        Dim Builder As New StringBuilder
        For Each Pair In Parameters
            Builder.Append(If(Builder.Length = 0, "?", "&"))
            Builder.Append(Uri.EscapeDataString(Pair.Key))
            Builder.Append("=")
            Builder.Append(Uri.EscapeDataString(If(Pair.Value, "")))
        Next
        Return Builder.ToString()
    End Function

    Private NodesCache As List(Of String) = Nothing

    ''' <summary>
    ''' 获取公共节点列表。失败时返回空列表。
    ''' </summary>
    Public Function FetchNodes() As List(Of String)
        If NodesCache IsNot Nothing Then Return NodesCache
        Dim Result As New List(Of String)
        Try
            Dim Json = JArray.Parse(HttpGet(NodeListUrl, 10000))
            For Each Item As JObject In Json
                Dim Url As String = TryCast(Item("url"), JValue)?.Value?.ToString
                If Not String.IsNullOrWhiteSpace(Url) Then Result.Add(Url)
            Next
            Logger.Info($"获取到 {Result.Count} 个联机节点")
        Catch ex As Exception
            Logger.Warn(ex, "获取联机节点列表失败，将仅尝试直连")
        End Try
        NodesCache = Result
        Return Result
    End Function

    ''' <summary>
    ''' 当前玩家的名称，用于在房间中显示。
    ''' </summary>
    Public ReadOnly Property PlayerName As String
        Get
            Try
                Select Case Settings.Get(Of McLoginType)("LoginType")
                    Case McLoginType.Ms
                        Dim Name As String = Settings.Get(Of String)("CacheMsV2Name")
                        If String.IsNullOrWhiteSpace(Name) Then Name = Settings.Get(Of String)("CacheMsName")
                        If Not String.IsNullOrWhiteSpace(Name) Then Return Name
                    Case McLoginType.Legacy
                        Dim Name As String = Settings.Get(Of String)("LoginLegacyName")
                        If Not String.IsNullOrWhiteSpace(Name) Then Return Name.Split("¨"c)(0)
                    Case McLoginType.Nide
                        Dim Name As String = Settings.Get(Of String)("CacheNideName")
                        If Not String.IsNullOrWhiteSpace(Name) Then Return Name
                    Case McLoginType.Auth
                        Dim Name As String = Settings.Get(Of String)("CacheAuthName")
                        If Not String.IsNullOrWhiteSpace(Name) Then Return Name
                End Select
            Catch ex As Exception
                Logger.Warn(ex, "获取玩家名称失败")
            End Try
            Return Environment.UserName
        End Get
    End Property

    ''' <summary>
    ''' 通知联机模块回到空闲状态。
    ''' </summary>
    Public Sub SetWaiting()
        Dim CurrentPort As Integer = LinkPort
        If CurrentPort <= 0 Then
            ResetState()
            Return
        End If
        RunInThread(
        Sub()
            Try
                HttpGet($"http://127.0.0.1:{CurrentPort}/state/ide", 10000)
            Catch ex As Exception
                Logger.Warn(ex, "重置联机模块状态失败")
            End Try
        End Sub)
        PublishState(New LinkStatus With {.Kind = LinkStatusKind.Waiting, .Port = CurrentPort, .Index = -1})
    End Sub

    ''' <summary>
    ''' 开始扫描局域网端口（房主）。
    ''' </summary>
    Public Sub StartScanning()
        Dim CurrentPort As Integer = LinkPort
        If CurrentPort <= 0 Then Throw New Exception("$联机模块尚未启动。")
        PublishState(New LinkStatus With {.Kind = LinkStatusKind.HostScanning, .Port = CurrentPort, .Index = -1})
        Dim Query As New List(Of KeyValuePair(Of String, String)) From {
            New KeyValuePair(Of String, String)("player", PlayerName)
        }
        For Each Node In FetchNodes()
            Query.Add(New KeyValuePair(Of String, String)("public_nodes", Node))
        Next
        Logger.Info($"开始扫描局域网端口，玩家名 {PlayerName}")
        Try
            HttpGet($"http://127.0.0.1:{CurrentPort}/state/scanning" & BuildQuery(Query), 30000)
        Catch ex As Exception
            ex.ThrowIfCanceled()
            If GetHttpStatusCode(ex) = 400 Then Throw New Exception("$联机模块拒绝了本次请求，请稍后重试。", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' 加入指定房间（房客）。
    ''' </summary>
    Public Sub StartGuesting(Room As String)
        Dim CurrentPort As Integer = LinkPort
        If CurrentPort <= 0 Then Throw New Exception("$联机模块尚未启动。")
        PublishState(New LinkStatus With {.Kind = LinkStatusKind.GuestConnecting, .Port = CurrentPort, .Index = -1})
        Dim Query As New List(Of KeyValuePair(Of String, String)) From {
            New KeyValuePair(Of String, String)("room", Room),
            New KeyValuePair(Of String, String)("player", PlayerName)
        }
        For Each Node In FetchNodes()
            Query.Add(New KeyValuePair(Of String, String)("public_nodes", Node))
        Next
        Logger.Info($"尝试加入房间 {Room}，玩家名 {PlayerName}")
        Try
            HttpGet($"http://127.0.0.1:{CurrentPort}/state/guesting" & BuildQuery(Query), 30000)
        Catch ex As Exception
            ex.ThrowIfCanceled()
            If GetHttpStatusCode(ex) = 400 Then
                Throw New Exception($"$房间号无效！{vbCrLf}请向房主确认房间号是否正确，注意区分大小写。", ex)
            End If
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' 从异常中提取 HTTP 状态码，非 HTTP 异常返回 -1。
    ''' </summary>
    Private Function GetHttpStatusCode(Ex As Exception) As Integer
        Dim Current As Exception = Ex
        While Current IsNot Nothing
            Dim WebEx = TryCast(Current, WebException)
            If WebEx IsNot Nothing Then
                Dim Response = TryCast(WebEx.Response, HttpWebResponse)
                If Response IsNot Nothing Then Return CInt(Response.StatusCode)
            End If
            Current = Current.InnerException
        End While
        Return -1
    End Function

    ''' <summary>
    ''' 读取一次状态。
    ''' </summary>
    Public Function ReadState(CurrentPort As Integer, Optional Timeout As Integer = 10000) As LinkStatus
        Dim Json As JObject = JObject.Parse(HttpGet($"http://127.0.0.1:{CurrentPort}/state", Timeout))
        Return ParseState(Json, CurrentPort)
    End Function

    Private Function ParseState(Json As JObject, CurrentPort As Integer) As LinkStatus
        Dim Result As New LinkStatus With {.Port = CurrentPort}
        Result.Index = If(Json("index") Is Nothing, -1, Json("index").ToObject(Of Integer))
        Result.RawState = Json("state")?.ToString
        Select Case Result.RawState
            Case "waiting"
                Result.Kind = LinkStatusKind.Waiting
            Case "host-scanning"
                Result.Kind = LinkStatusKind.HostScanning
            Case "host-starting"
                Result.Kind = LinkStatusKind.HostStarting
            Case "host-ok"
                Result.Kind = LinkStatusKind.HostOK
                Result.RoomCode = Json("room")?.ToString
                ReadProfiles(Json, Result)
            Case "guest-connecting"
                Result.Kind = LinkStatusKind.GuestConnecting
            Case "guest-starting"
                Result.Kind = LinkStatusKind.GuestStarting
                Result.Difficulty = If(Json("difficulty") Is Nothing, -1, Json("difficulty").ToObject(Of Integer))
            Case "guest-ok"
                Result.Kind = LinkStatusKind.GuestOK
                Result.Url = Json("url")?.ToString
                ReadProfiles(Json, Result)
            Case "exception"
                Result.Kind = LinkStatusKind.ExceptionState
                Result.ExceptionType = If(Json("type") Is Nothing, -1, Json("type").ToObject(Of Integer))
            Case Else
                Result.Kind = LinkStatusKind.Unknown
        End Select
        Return Result
    End Function

    Private Sub ReadProfiles(Json As JObject, Target As LinkStatus)
        Target.Profiles.Clear()
        Dim Arr = TryCast(Json("profiles"), JArray)
        If Arr Is Nothing Then Return
        For Each Item As JObject In Arr
            Dim Name As String = Item("name")?.ToString
            If Not String.IsNullOrWhiteSpace(Name) AndAlso Not Target.Profiles.Contains(Name) Then Target.Profiles.Add(Name)
        Next
    End Sub

    ''' <summary>
    ''' 在后台持续跟踪状态，直到联机结束。
    ''' </summary>
    Private Watcher As Thread = Nothing
    Private WatcherRunning As Boolean = False

    Public Sub StartWatcher()
        If WatcherRunning Then Return
        WatcherRunning = True
        Watcher = New Thread(AddressOf WatcherLoop) With {.IsBackground = True, .Name = "PCL Link Watcher"}
        Watcher.Start()
    End Sub

    Public Sub StopWatcher()
        WatcherRunning = False
        Watcher = Nothing
    End Sub

    Private Sub WatcherLoop()
        Dim FailCount As Integer = 0
        Do While WatcherRunning
            Thread.Sleep(1000)
            If Not WatcherRunning Then Exit Do
            Dim Snapshot As LinkStatus = State
            If Not Snapshot.IsConnected OrElse Snapshot.Port <= 0 Then Exit Do
            Try
                Dim NextState As LinkStatus = ReadState(Snapshot.Port, 8000)
                FailCount = 0
                If NextState.Index <= Snapshot.Index Then Continue Do
                PublishState(NextState)
            Catch ex As Exception
                '连续多次失败才认定联机模块已经退出，避免偶发的请求超时打断房间
                FailCount += 1
                Logger.Warn(ex, $"读取联机状态失败（第 {FailCount} 次）")
                If FailCount >= 5 Then
                    PublishState(New LinkStatus With {.Kind = LinkStatusKind.Fatal, .Port = Snapshot.Port, .RawState = "联机模块已断开"})
                    Exit Do
                End If
            End Try
        Loop
        WatcherRunning = False
    End Sub

    ''' <summary>
    ''' 联机模块进程是否仍在运行。副模式下端口由更早启动的进程提供，因此不能作为判断依据。
    ''' </summary>
    Public Function IsProcessAlive() As Boolean
        Try
            If LinkProcess IsNot Nothing AndAlso Not LinkProcess.HasExited Then Return True
            Return Process.GetProcessesByName($"terracotta-{LinkVersion}-{LinkClassifier}").Any()
        Catch
            Return False
        End Try
    End Function

End Module
