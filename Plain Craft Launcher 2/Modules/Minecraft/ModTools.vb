'百宝箱用到的底层能力：服务器状态查询、皮肤头像生成、成就卡片绘制、物品图标获取。

Imports System.Diagnostics
Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports Newtonsoft.Json.Linq
Imports Dr = System.Drawing
Imports Dr2D = System.Drawing.Drawing2D
Imports DrImg = System.Drawing.Imaging
Imports DrText = System.Drawing.Text

Public Module ModTools

#Region "服务器状态查询"

    ''' <summary>
    ''' 一次服务器列表查询（Server List Ping）的结果。
    ''' </summary>
    Public Class ServerPingResult
        ''' <summary>服务器返回的版本名，如 1.21.1。</summary>
        Public VersionName As String = ""
        ''' <summary>协议版本号，未知为 -1。</summary>
        Public Protocol As Integer = -1
        ''' <summary>当前在线人数，未知为 -1。</summary>
        Public Online As Integer = -1
        ''' <summary>人数上限，未知为 -1。</summary>
        Public Max As Integer = -1
        ''' <summary>MOTD 的原始文本（已转换为 § 颜色码格式）。</summary>
        Public MotdRaw As String = ""
        ''' <summary>图标的数据 URI（data:image/png;base64,...），没有则为空。</summary>
        Public Favicon As String = ""
        ''' <summary>往返延迟（毫秒）。</summary>
        Public Latency As Integer = -1
        ''' <summary>在线玩家名样本。</summary>
        Public SamplePlayers As New List(Of String)
    End Class

    ''' <summary>
    ''' 向指定地址发起服务器列表查询。失败时抛出异常，异常信息可直接展示给用户。
    ''' </summary>
    ''' <param name="Address">服务器地址，不支持自动解析 SRV 记录。</param>
    ''' <param name="Port">服务器端口。</param>
    ''' <param name="Timeout">总超时（毫秒）。</param>
    Public Function ServerPing(Address As String, Port As Integer, Optional Timeout As Integer = 8000) As ServerPingResult
        Dim Result As New ServerPingResult
        Dim Client As TcpClient = Nothing
        Try
            '建立连接
            Client = New TcpClient With {.ReceiveTimeout = Timeout, .SendTimeout = Timeout}
            Dim ConnectTask = Client.ConnectAsync(Address, Port)
            If Not ConnectTask.Wait(Timeout) Then Throw New TimeoutException("连接超时")
            If ConnectTask.IsFaulted Then
                Dim Reason As String = If(ConnectTask.Exception?.GetBaseException()?.Message, "未知原因")
                Throw New Exception($"无法连接到服务器（{Reason}）")
            End If
            Dim Net As NetworkStream = Client.GetStream()

            '握手包
            Using Handshake As New MemoryStream
                WriteVarInt(Handshake, 0)          '包 ID：Handshake
                WriteVarInt(Handshake, 767)        '协议版本，取一个较新的值
                WriteString(Handshake, Address)
                Handshake.WriteByte(CByte((Port >> 8) And &HFF))
                Handshake.WriteByte(CByte(Port And &HFF))
                WriteVarInt(Handshake, 1)          '下一状态：Status
                Dim Payload As Byte() = Handshake.ToArray()
                Using Packet As New MemoryStream
                    WriteVarInt(Packet, Payload.Length)
                    Packet.Write(Payload, 0, Payload.Length)
                    '紧接着发 Status Request（长度 1，包 ID 0）
                    WriteVarInt(Packet, 1)
                    WriteVarInt(Packet, 0)
                    Dim Buffer As Byte() = Packet.ToArray()
                    Net.Write(Buffer, 0, Buffer.Length)
                    Net.Flush()
                End Using
            End Using

            '计时并读取响应
            Dim Watch As Stopwatch = Stopwatch.StartNew()
            Dim Length As Integer = ReadVarInt(Net)
            If Length <= 0 Then Throw New Exception("服务器返回了空响应")
            Dim Data(Length - 1) As Byte
            ReadFully(Net, Data, Length)
            Watch.Stop()
            Result.Latency = CInt(Watch.ElapsedMilliseconds)

            '解析数据包
            Dim Pos As Integer = 0
            Dim PacketId As Integer = ReadVarIntFrom(Data, Pos)
            If PacketId <> 0 Then Throw New Exception($"服务器返回了非预期的数据包（ID={PacketId}）")
            Dim JsonLength As Integer = ReadVarIntFrom(Data, Pos)
            If JsonLength <= 0 OrElse Pos + JsonLength > Data.Length Then Throw New Exception("服务器返回的数据不完整")
            Dim JsonText As String = Encoding.UTF8.GetString(Data, Pos, JsonLength)
            Dim Json As JObject
            Try
                Json = JObject.Parse(JsonText)
            Catch ex As Exception
                Throw New Exception("无法解析服务器返回的内容：" & ex.Message)
            End Try

            '版本
            Dim VersionToken As JToken = Json("version")
            If VersionToken IsNot Nothing Then
                Result.VersionName = If(VersionToken("name"), "").ToString
                Result.Protocol = Val(If(VersionToken("protocol"), -1).ToString)
            End If
            '人数
            Dim PlayersToken As JToken = Json("players")
            If PlayersToken IsNot Nothing Then
                Result.Online = Val(If(PlayersToken("online"), -1).ToString)
                Result.Max = Val(If(PlayersToken("max"), -1).ToString)
                Dim Sample As JArray = TryCast(PlayersToken("sample"), JArray)
                If Sample IsNot Nothing Then
                    For Each Entry As JToken In Sample
                        Dim Name As String = If(Entry("name"), "").ToString
                        If Not String.IsNullOrEmpty(Name) Then Result.SamplePlayers.Add(Name)
                    Next
                End If
            End If
            'MOTD
            Result.MotdRaw = ChatToLegacy(Json("description"))
            '图标
            Dim FaviconToken As JToken = Json("favicon")
            If FaviconToken IsNot Nothing Then
                Dim FaviconText As String = FaviconToken.ToString
                If FaviconText.StartsWithF("data:image/png;base64,", True) Then Result.Favicon = FaviconText
            End If

            Return Result
        Catch ex As SocketException
            Throw New Exception($"网络错误（{ex.SocketErrorCode}）：{ex.Message}")
        Finally
            Try
                If Client IsNot Nothing Then Client.Close()
            Catch ex As Exception
                Logger.Warn(ex, "关闭服务器查询连接失败")
            End Try
        End Try
    End Function

    ''' <summary>
    ''' 写入 Minecraft 协议的 VarInt 编码。
    ''' </summary>
    Private Sub WriteVarInt(Stream As Stream, Value As Integer)
        Dim Remaining As UInteger = CUInt(Value) And &HFFFFFFFFUI
        Do
            Dim Temp As Byte = CByte(Remaining And &H7FUI)
            Remaining >>= 7
            If Remaining <> 0 Then Temp = Temp Or &H80
            Stream.WriteByte(Temp)
        Loop While Remaining <> 0
    End Sub

    ''' <summary>
    ''' 写入 Minecraft 协议的字符串（长度前缀 + UTF-8）。
    ''' </summary>
    Private Sub WriteString(Stream As Stream, Text As String)
        Dim Bytes As Byte() = Encoding.UTF8.GetBytes(Text)
        WriteVarInt(Stream, Bytes.Length)
        Stream.Write(Bytes, 0, Bytes.Length)
    End Sub

    ''' <summary>
    ''' 从网络流中读取一个 VarInt。
    ''' </summary>
    Private Function ReadVarInt(Stream As Stream) As Integer
        Dim Result As Integer = 0
        Dim Shift As Integer = 0
        Do
            Dim Current As Integer = Stream.ReadByte()
            If Current < 0 Then Throw New Exception("服务器在返回数据前关闭了连接")
            Result = Result Or ((Current And &H7F) << Shift)
            Shift += 7
            If Shift > 35 Then Throw New Exception("服务器返回了非法的数据长度")
            If (Current And &H80) = 0 Then Exit Do
        Loop
        Return Result
    End Function

    ''' <summary>
    ''' 从字节数组中读取一个 VarInt，并后移 <paramref name="Position"/>。
    ''' </summary>
    Private Function ReadVarIntFrom(Data As Byte(), ByRef Position As Integer) As Integer
        Dim Result As Integer = 0
        Dim Shift As Integer = 0
        Do
            If Position >= Data.Length Then Throw New Exception("服务器返回的数据不完整")
            Dim Current As Integer = Data(Position)
            Position += 1
            Result = Result Or ((Current And &H7F) << Shift)
            Shift += 7
            If Shift > 35 Then Throw New Exception("服务器返回了非法的数据长度")
            If (Current And &H80) = 0 Then Exit Do
        Loop
        Return Result
    End Function

    ''' <summary>
    ''' 从网络流中读满指定字节数。
    ''' </summary>
    Private Sub ReadFully(Stream As Stream, Buffer As Byte(), Count As Integer)
        Dim Offset As Integer = 0
        Do While Offset < Count
            Dim Read As Integer = Stream.Read(Buffer, Offset, Count - Offset)
            If Read <= 0 Then Throw New Exception("服务器在传输过程中关闭了连接")
            Offset += Read
        Loop
    End Sub

#End Region

#Region "MOTD 文本处理"

    ''' <summary>
    ''' 把 MOTD 中的一段文本。
    ''' </summary>
    Public Class MotdSegment
        Public Text As String = ""
        ''' <summary>形如 #AAAAAA 的颜色。</summary>
        Public Color As String = "#AAAAAA"
        Public Bold As Boolean = False
    End Class

    Private ReadOnly MotdColorMap As New Dictionary(Of String, String) From {
        {"0", "#000000"}, {"1", "#0000AA"}, {"2", "#00AA00"}, {"3", "#00AAAA"},
        {"4", "#AA0000"}, {"5", "#AA00AA"}, {"6", "#FFAA00"}, {"7", "#AAAAAA"},
        {"8", "#555555"}, {"9", "#5555FF"}, {"a", "#55FF55"}, {"b", "#55FFFF"},
        {"c", "#FF5555"}, {"d", "#FF55FF"}, {"e", "#FFFF55"}, {"f", "#FFFFFF"}
    }

    ''' <summary>
    ''' 把服务器返回的 MOTD（可能是字符串、聊天组件或组件数组）转换为 § 颜色码文本。
    ''' </summary>
    Public Function ChatToLegacy(Token As JToken) As String
        If Token Is Nothing Then Return ""
        Select Case Token.Type
            Case JTokenType.String
                Return Token.ToString
            Case JTokenType.Array
                Dim Sb As New StringBuilder
                For Each Item As JToken In Token
                    Sb.Append(ChatToLegacy(Item))
                Next
                Return Sb.ToString
            Case JTokenType.Object
                Dim Sb As New StringBuilder
                Dim Prefix As New StringBuilder
                Dim ColorToken As JToken = Token("color")
                If ColorToken IsNot Nothing Then
                    Dim Code As String = ChatColorCode(ColorToken.ToString)
                    If Code IsNot Nothing Then Prefix.Append("§" & Code)
                End If
                If ChatFlagIsSet(Token("bold")) Then Prefix.Append("§l")
                If ChatFlagIsSet(Token("italic")) Then Prefix.Append("§o")
                If ChatFlagIsSet(Token("underlined")) Then Prefix.Append("§n")
                If ChatFlagIsSet(Token("strikethrough")) Then Prefix.Append("§m")
                If Prefix.Length > 0 Then Sb.Append(Prefix.ToString)
                Dim TextToken As JToken = Token("text")
                If TextToken IsNot Nothing Then Sb.Append(TextToken.ToString)
                Dim TranslateToken As JToken = Token("translate")
                If TextToken Is Nothing AndAlso TranslateToken IsNot Nothing Then Sb.Append(TranslateToken.ToString)
                Dim WithToken As JArray = TryCast(Token("with"), JArray)
                If WithToken IsNot Nothing Then
                    For Each Item As JToken In WithToken
                        Sb.Append(ChatToLegacy(Item))
                    Next
                End If
                Dim ExtraToken As JArray = TryCast(Token("extra"), JArray)
                If ExtraToken IsNot Nothing Then
                    For Each Item As JToken In ExtraToken
                        Sb.Append(ChatToLegacy(Item))
                    Next
                End If
                If Prefix.Length > 0 Then Sb.Append("§r")
                Return Sb.ToString
            Case Else
                Return Token.ToString
        End Select
    End Function

    ''' <summary>
    ''' 把颜色名转换为 § 颜色码字符。
    ''' </summary>
    Private Function ChatColorCode(Name As String) As String
        If String.IsNullOrEmpty(Name) Then Return Nothing
        Select Case Name.Lower
            Case "black" : Return "0"
            Case "dark_blue" : Return "1"
            Case "dark_green" : Return "2"
            Case "dark_aqua" : Return "3"
            Case "dark_red" : Return "4"
            Case "dark_purple" : Return "5"
            Case "gold" : Return "6"
            Case "gray", "grey" : Return "7"
            Case "dark_gray", "dark_grey" : Return "8"
            Case "blue" : Return "9"
            Case "green" : Return "a"
            Case "aqua" : Return "b"
            Case "red" : Return "c"
            Case "light_purple" : Return "d"
            Case "yellow" : Return "e"
            Case "white" : Return "f"
            Case "minecoin_gold" : Return "6"
            Case Else
                '支持 #RRGGBB 形式的自定义颜色，映射到最接近的原版色
                If Name.StartsWithF("#") AndAlso Name.Length = 7 Then Return NearestColorCode(Name)
                Return Nothing
        End Select
    End Function

    ''' <summary>
    ''' 把一个 #RRGGBB 颜色映射到最接近的原版颜色码。
    ''' </summary>
    Private Function NearestColorCode(Hex As String) As String
        Try
            Dim R As Integer = Convert.ToInt32(Hex.Substring(1, 2), 16)
            Dim G As Integer = Convert.ToInt32(Hex.Substring(3, 2), 16)
            Dim B As Integer = Convert.ToInt32(Hex.Substring(5, 2), 16)
            Dim BestCode As String = "f"
            Dim BestDistance As Integer = Integer.MaxValue
            For Each Pair In MotdColorMap
                Dim C As Dr.Color = Dr.ColorTranslator.FromHtml(Pair.Value)
                Dim Distance As Integer = (R - C.R) ^ 2 + (G - C.G) ^ 2 + (B - C.B) ^ 2
                If Distance < BestDistance Then
                    BestDistance = Distance
                    BestCode = Pair.Key
                End If
            Next
            Return BestCode
        Catch ex As Exception
            Return "f"
        End Try
    End Function

    ''' <summary>
    ''' 判断聊天组件中的布尔标记是否为真。
    ''' </summary>
    Private Function ChatFlagIsSet(Token As JToken) As Boolean
        If Token Is Nothing Then Return False
        If Token.Type = JTokenType.Boolean Then Return CBool(Token)
        Return Token.ToString.Lower = "true"
    End Function

    ''' <summary>
    ''' 把 § 颜色码文本切分为可分别着色的片段。
    ''' </summary>
    Public Function MotdToSegments(Legacy As String) As List(Of MotdSegment)
        Dim Result As New List(Of MotdSegment)
        If String.IsNullOrEmpty(Legacy) Then Return Result
        Dim CurrentColor As String = "#AAAAAA"
        Dim CurrentBold As Boolean = False
        Dim Buffer As New StringBuilder
        Dim Index As Integer = 0
        Do While Index < Legacy.Length
            Dim C As Char = Legacy(Index)
            If C = "§"c AndAlso Index + 1 < Legacy.Length Then
                '输出缓冲区中已有的内容
                If Buffer.Length > 0 Then
                    Result.Add(New MotdSegment With {.Text = Buffer.ToString, .Color = CurrentColor, .Bold = CurrentBold})
                    Buffer.Clear()
                End If
                Dim Code As String = Legacy(Index + 1).ToString.Lower
                If MotdColorMap.ContainsKey(Code) Then
                    CurrentColor = MotdColorMap(Code)
                    CurrentBold = False '原版中颜色码会重置格式
                ElseIf Code = "l" Then
                    CurrentBold = True
                ElseIf Code = "r" Then
                    CurrentColor = "#AAAAAA"
                    CurrentBold = False
                End If
                Index += 2
            Else
                Buffer.Append(C)
                Index += 1
            End If
        Loop
        If Buffer.Length > 0 Then
            Result.Add(New MotdSegment With {.Text = Buffer.ToString, .Color = CurrentColor, .Bold = CurrentBold})
        End If
        Return Result
    End Function

    ''' <summary>
    ''' 去掉 § 颜色码，得到纯文本。
    ''' </summary>
    Public Function MotdToPlain(Legacy As String) As String
        Return MotdToSegments(Legacy).Select(Function(s) s.Text).Join("")
    End Function

#End Region

#Region "皮肤头像生成"

    ''' <summary>
    ''' 从皮肤文件中裁出正方形头像：正脸打底，第二层（帽子层）按 alpha 叠加。
    ''' </summary>
    ''' <param name="SkinPath">皮肤 PNG 路径。</param>
    ''' <param name="HeadSize">输出边长（像素）。</param>
    Public Function SkinHeadRender(SkinPath As String, HeadSize As Integer) As BitmapSource
        If Not File.Exists(SkinPath) Then Throw New Exception("皮肤文件不存在")
        If HeadSize < 8 Then Throw New Exception("头像尺寸过小")
        Dim Skin As Dr.Bitmap = Nothing
        Try
            '直接读流，避免锁住原文件
            Using Stream As New FileStream(SkinPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Skin = New Dr.Bitmap(Stream)
            End Using
            If Skin.Width <> Skin.Height Then Throw New Exception("皮肤图片必须是正方形")
            If Skin.Width < 64 OrElse Skin.Width Mod 64 <> 0 Then Throw New Exception("皮肤图片的边长必须是 64 的整数倍")
            Dim Scale As Integer = Skin.Width \ 64

            Dim Result As New Dr.Bitmap(HeadSize, HeadSize, DrImg.PixelFormat.Format32bppArgb)
            Try
                Using G As Dr.Graphics = Dr.Graphics.FromImage(Result)
                    G.InterpolationMode = Dr2D.InterpolationMode.NearestNeighbor
                    G.PixelOffsetMode = Dr2D.PixelOffsetMode.Half
                    G.CompositingMode = Dr2D.CompositingMode.SourceCopy
                    '正脸：8,8 起 8×8
                    Dim FaceRect As New Dr.Rectangle(8 * Scale, 8 * Scale, 8 * Scale, 8 * Scale)
                    G.DrawImage(Skin, New Dr.Rectangle(0, 0, HeadSize, HeadSize), FaceRect, Dr.GraphicsUnit.Pixel)
                End Using
                '第二层（帽子）：40,8 起 8×8，逐像素 alpha 叠加
                If Skin.Width >= 64 Then
                    Using Hat As New Dr.Bitmap(HeadSize, HeadSize, DrImg.PixelFormat.Format32bppArgb)
                        Using G As Dr.Graphics = Dr.Graphics.FromImage(Hat)
                            G.InterpolationMode = Dr2D.InterpolationMode.NearestNeighbor
                            G.PixelOffsetMode = Dr2D.PixelOffsetMode.Half
                            G.CompositingMode = Dr2D.CompositingMode.SourceCopy
                            Dim HatRect As New Dr.Rectangle(40 * Scale, 8 * Scale, 8 * Scale, 8 * Scale)
                            G.DrawImage(Skin, New Dr.Rectangle(0, 0, HeadSize, HeadSize), HatRect, Dr.GraphicsUnit.Pixel)
                        End Using
                        For X As Integer = 0 To HeadSize - 1
                            For Y As Integer = 0 To HeadSize - 1
                                Dim Pixel As Dr.Color = Hat.GetPixel(X, Y)
                                If Pixel.A > 0 Then Result.SetPixel(X, Y, Pixel)
                            Next
                        Next
                    End Using
                End If
                Return BitmapToBitmapSource(Result)
            Finally
                Result.Dispose()
            End Try
        Finally
            If Skin IsNot Nothing Then Skin.Dispose()
        End Try
    End Function

    ''' <summary>
    ''' 把一个 GDI+ 位图转换为可绑定到 WPF Image 的位图源。
    ''' </summary>
    Public Function BitmapToBitmapSource(Bitmap As Dr.Bitmap) As BitmapSource
        Using Stream As New MemoryStream
            Bitmap.Save(Stream, DrImg.ImageFormat.Png)
            Stream.Position = 0
            Dim Image As New BitmapImage()
            Image.BeginInit()
            Image.CacheOption = BitmapCacheOption.OnLoad
            Image.StreamSource = Stream
            Image.EndInit()
            Image.Freeze()
            Return Image
        End Using
    End Function

    ''' <summary>
    ''' 把本地图片文件读成可绑定到 WPF Image 的位图源，不锁定文件。
    ''' </summary>
    Public Function FileToBitmapSource(Path As String) As BitmapSource
        Using Stream As New FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            Dim Image As New BitmapImage()
            Image.BeginInit()
            Image.CacheOption = BitmapCacheOption.OnLoad
            Image.StreamSource = Stream
            Image.EndInit()
            Image.Freeze()
            Return Image
        End Using
    End Function

#End Region

#Region "成就卡片绘制"

    ''' <summary>
    ''' 本地绘制一张 Minecraft 风格的成就达成卡片。
    ''' </summary>
    ''' <param name="Icon">物品图标，可为 Nothing（此时只画文字）。</param>
    ''' <param name="Title">成就名称。</param>
    ''' <param name="Line1">第一行描述。</param>
    ''' <param name="Line2">第二行描述，可为空。</param>
    Public Function AchievementRender(Icon As Dr.Bitmap, Title As String, Line1 As String, Line2 As String) As BitmapSource
        Dim Width As Integer = 480, Height As Integer = 96
        Dim Result As New Dr.Bitmap(Width, Height, DrImg.PixelFormat.Format32bppArgb)
        Try
            Using G As Dr.Graphics = Dr.Graphics.FromImage(Result)
                G.SmoothingMode = Dr2D.SmoothingMode.None
                G.TextRenderingHint = DrText.TextRenderingHint.ClearTypeGridFit
                G.InterpolationMode = Dr2D.InterpolationMode.NearestNeighbor
                G.PixelOffsetMode = Dr2D.PixelOffsetMode.Half
                '背景：上深下浅的灰色渐变，与游戏内弹出框接近
                Using Brush As New Dr2D.LinearGradientBrush(New Dr.Rectangle(0, 0, Width, Height),
                                                            Dr.Color.FromArgb(255, 60, 60, 60),
                                                            Dr.Color.FromArgb(255, 33, 33, 33),
                                                            Dr2D.LinearGradientMode.Vertical)
                    G.FillRectangle(Brush, 0, 0, Width, Height)
                End Using
                '外框与内侧高光
                Using Pen As New Dr.Pen(Dr.Color.FromArgb(255, 16, 16, 16), 2)
                    G.DrawRectangle(Pen, 1, 1, Width - 3, Height - 3)
                End Using
                Using Pen As New Dr.Pen(Dr.Color.FromArgb(90, 255, 255, 255), 1)
                    G.DrawRectangle(Pen, 3, 3, Width - 7, Height - 7)
                End Using
                '图标
                Dim TextLeft As Integer = 20
                If Icon IsNot Nothing Then
                    Dim Box As New Dr.Rectangle(20, 20, 56, 56)
                    Using Back As New Dr.SolidBrush(Dr.Color.FromArgb(70, 0, 0, 0))
                        G.FillRectangle(Back, Box)
                    End Using
                    G.DrawImage(Icon, Box)
                    TextLeft = 92
                End If
                '文字
                Using TitleFont As New Dr.Font("Microsoft YaHei UI", 15.5F, Dr.FontStyle.Bold, Dr.GraphicsUnit.Pixel)
                    Using TitleBrush As New Dr.SolidBrush(Dr.Color.FromArgb(255, 255, 255, 255))
                        G.DrawString(If(String.IsNullOrEmpty(Title), "成就达成！", Title), TitleFont, TitleBrush, TextLeft, 15)
                    End Using
                End Using
                Using LineFont As New Dr.Font("Microsoft YaHei UI", 12.5F, Dr.FontStyle.Regular, Dr.GraphicsUnit.Pixel)
                    Using LineBrush As New Dr.SolidBrush(Dr.Color.FromArgb(255, 170, 170, 170))
                        If Not String.IsNullOrEmpty(Line1) Then G.DrawString(Line1, LineFont, LineBrush, TextLeft, 42)
                        If Not String.IsNullOrEmpty(Line2) Then G.DrawString(Line2, LineFont, LineBrush, TextLeft, 62)
                    End Using
                End Using
            End Using
            Return BitmapToBitmapSource(Result)
        Finally
            Result.Dispose()
        End Try
    End Function

#End Region

#Region "物品图标获取"

    ''' <summary>
    ''' 获取一个物品或方块的图标（32×32），返回本地缓存路径，失败时返回空字符串。
    ''' 会自动尝试物品图标与方块图标，并依次尝试多个下载源。
    ''' </summary>
    ''' <param name="Id">物品 ID，可以带 minecraft: 前缀。</param>
    Public Function McItemIconGet(Id As String) As String
        Dim CleanId As String = If(Id, "").Trim.Lower
        If CleanId.StartsWithF("minecraft:") Then CleanId = CleanId.Substring(10)
        If String.IsNullOrEmpty(CleanId) Then Return ""
        '只允许安全字符，避免拼出奇怪的地址
        For Each C As Char In CleanId
            Dim IsSafe As Boolean = Char.IsLetterOrDigit(C) OrElse C = "_"c OrElse C = "."c OrElse C = "-"c OrElse C = "/"c
            If Not IsSafe Then Throw New Exception("物品 ID 只能包含字母、数字、下划线、点与斜杠")
        Next
        Dim CacheFolder As String = PathTemp & "ToolIconCache\"
        DirectoryUtils.Create(CacheFolder)
        Dim CacheName As String = CleanId.Replace(":", "_").Replace("/", "_") & ".png"
        Dim CachePath As String = CacheFolder & CacheName
        If File.Exists(CachePath) AndAlso New FileInfo(CachePath).Length > 0 Then Return CachePath
        '依次尝试候选地址（国内优先走镜像，官方源兜底）
        Dim RawPath As String = "InventivetalentDev/minecraft-assets/1.20.1/assets/minecraft/textures"
        Dim Candidates As New List(Of String)
        For Each Kind In {"item", "block"}
            Candidates.Add($"https://ghproxy.net/https://raw.githubusercontent.com/{RawPath}/{Kind}/{CleanId}.png")
            Candidates.Add($"https://cdn.jsdelivr.net/gh/InventivetalentDev/minecraft-assets@1.20.1/assets/minecraft/textures/{Kind}/{CleanId}.png")
            Candidates.Add($"https://raw.githubusercontent.com/{RawPath}/{Kind}/{CleanId}.png")
        Next
        Dim Errors As New List(Of String)
        For Each Url In Candidates
            Try
                Dim Bytes As Byte() = HttpGetBytes(Url, 5000, 512 * 1024)
                If Bytes.Length < 8 OrElse Bytes(0) <> &H89 OrElse Bytes(1) <> &H50 Then Throw New Exception("下载到的内容不是图片")
                Dim TempPath As String = CachePath & ".tmp"
                FileUtils.Write(TempPath, Bytes)
                If File.Exists(CachePath) Then File.Delete(CachePath)
                File.Move(TempPath, CachePath)
                Logger.Info($"已缓存物品图标：{CleanId}（{Url}）")
                Return CachePath
            Catch ex As Exception
                Errors.Add(Url & "：" & ex.Message)
            End Try
        Next
        Logger.Warn($"获取物品图标失败：{CleanId}" & vbCrLf & Errors.Join(vbCrLf))
        Return ""
    End Function

    ''' <summary>
    ''' 带超时与大小上限的简单下载，返回字节数组。失败时抛出异常。
    ''' </summary>
    Private Function HttpGetBytes(Url As String, Timeout As Integer, MaxSize As Integer) As Byte()
        Dim Req As HttpWebRequest = CType(WebRequest.Create(Url), HttpWebRequest)
        Req.Method = "GET"
        Req.Timeout = Timeout
        Req.ReadWriteTimeout = Timeout
        Req.AllowAutoRedirect = True
        Req.UserAgent = "PCL2/" & VersionBaseName
        Using Resp As HttpWebResponse = CType(Req.GetResponse(), HttpWebResponse)
            If CInt(Resp.StatusCode) <> 200 Then Throw New Exception($"服务器返回 {CInt(Resp.StatusCode)}")
            Using Source As Stream = Resp.GetResponseStream()
                Using Buffer As New MemoryStream
                    Dim Chunk(8191) As Byte
                    Do
                        Dim Read As Integer = Source.Read(Chunk, 0, Chunk.Length)
                        If Read <= 0 Then Exit Do
                        Buffer.Write(Chunk, 0, Read)
                        If Buffer.Length > MaxSize Then Throw New Exception("下载到的内容过大")
                    Loop
                    Return Buffer.ToArray()
                End Using
            End Using
        End Using
    End Function

    ''' <summary>
    ''' 把物品图标加载为 WPF 位图源，失败时返回 Nothing。
    ''' </summary>
    Public Function McItemIconLoad(Id As String) As BitmapSource
        Try
            Dim Path As String = McItemIconGet(Id)
            If String.IsNullOrEmpty(Path) Then Return Nothing
            Return FileToBitmapSource(Path)
        Catch ex As Exception
            Logger.Warn(ex, $"加载物品图标失败：{Id}")
            Return Nothing
        End Try
    End Function

#End Region

End Module
