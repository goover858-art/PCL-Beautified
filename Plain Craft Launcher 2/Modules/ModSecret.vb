'由于包含加解密等安全信息，本文件中的部分代码已被删除

Friend Module ModSecret

    '标注 PCL 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "OpenSource"
    '在开源版的注册表与常规版的注册表隔离，以防数据冲突
    Public Const RegFolder As String = "PCLDebug"
    '用于微软登录的 ClientId（环境变量 PCL_MS_CLIENT_ID 可覆盖）
    Public OAuthClientId As String = If(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"), "")
    'CurseForge API Key（环境变量 PCL_CURSEFORGE_API_KEY 可覆盖）
    Public CurseForgeAPIKey As String = If(Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY"), "")
    '用于匿名数据收集的腾讯云日志服务上报 URL，形如 https://{region}.cls.tencentcs.com/track?topic_id={topic_id}
    Public Const ClsBaseUrl As String = ""

#Region "网络鉴权"

    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If Not UrlWithMark.EndsWithF("{CDN}") Then Return UrlWithMark
        Return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
    End Function
    ''' <summary>
    ''' 设置 Headers 的 UA、Referer。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        If Not Req.Headers.UserAgent.Any Then
            If Url.Contains("baidupcs.com") OrElse Url.Contains("baidu.com") Then
                Req.Headers.Add("User-Agent", "LogStatistic")  '#4951
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
            End If
        End If
        If Not SimulateBrowserHeaders Then Req.Headers.Add("Referer", $"http://{VersionCode}.open.pcl2.server/")
        If Url.Contains("api.curseforge.com") OrElse Url.Contains("forgecdn.net") Then Req.Headers.Add("x-api-key", CurseForgeAPIKey)
    End Sub

#End Region

#Region "主题"

    Public Color1 As New MyColor(52, 61, 74)
    Public Color2 As New MyColor(11, 91, 203)
    Public Color3 As New MyColor(19, 112, 243)
    Public Color4 As New MyColor(72, 144, 245)
    Public Color5 As New MyColor(150, 192, 249)
    Public Color6 As New MyColor(213, 230, 253)
    Public Color7 As New MyColor(222, 236, 253)
    Public Color8 As New MyColor(234, 242, 254)
    Public ColorBg0 As New MyColor(150, 192, 249)
    Public ColorBg1 As New MyColor(190, Color7)
    Public ColorGray1 As New MyColor(64, 64, 64)
    Public ColorGray2 As New MyColor(115, 115, 115)
    Public ColorGray3 As New MyColor(140, 140, 140)
    Public ColorGray4 As New MyColor(166, 166, 166)
    Public ColorGray5 As New MyColor(204, 204, 204)
    Public ColorGray6 As New MyColor(235, 235, 235)
    Public ColorGray7 As New MyColor(240, 240, 240)
    Public ColorGray8 As New MyColor(245, 245, 245)
    Public ColorSemiTransparent As New MyColor(1, Color8)

    Public ThemeNow As Integer = -1
    Public ColorHue As Integer = 210, ColorSat As Integer = 85, ColorLightAdjust As Integer = 0, ColorHueTopbarDelta As OneOf(Of Integer, Integer()) = 0
    Public ThemeDontClick As Integer = 0
    Public IsDark As Boolean = False

    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1)
        Try
            If ThemeNow = NewTheme AndAlso NewTheme >= 0 Then Return
            If NewTheme >= 0 Then
                ThemeNow = NewTheme

                '按主题序号设定预设 HSL（开源版被删除，这里自行恢复/补全；自定义主题 14 由 HSL 滑杆驱动）
                Select Case NewTheme
                    Case 0  '龙猫蓝
                        ColorHue = 210 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
                    Case 1  '甜柠青
                        ColorHue = 165 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
                    Case 2  '小草绿
                        ColorHue = 130 : ColorSat = 80 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
                    Case 3  '菠萝黄
                        ColorHue = 50 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
                    Case 4  '橡木棕
                        ColorHue = 30 : ColorSat = 55 : ColorLightAdjust = -5 : ColorHueTopbarDelta = 0
                    Case 5  '玄素黑（深色）
                        ColorHue = 210 : ColorSat = 35 : ColorLightAdjust = -16 : ColorHueTopbarDelta = 0
                    Case 6  '铁杆粉
                        ColorHue = 330 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
                    Case 7  '神秘紫
                        ColorHue = 272 : ColorSat = 80 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
                    Case 8  '秋仪金
                        ColorHue = 45 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
                    Case 9  '活跃橙
                        ColorHue = 22 : ColorSat = 88 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
                    Case 10 '跳票红
                        ColorHue = 0 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
                    Case 11 '极客蓝
                        ColorHue = 200 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
                    Case 12 '滑稽彩
                        ColorHue = 280 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 70
                    Case 13 '欧皇彩
                        ColorHue = 120 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 90
                    Case 14 '自定义：从设置读取 HSL（由 HSL 滑杆驱动）
                        ColorHue = Settings.Get(Of Integer)("UiLauncherHue")
                        ColorSat = Settings.Get(Of Integer)("UiLauncherSat")
                        ColorLightAdjust = Settings.Get(Of Integer)("UiLauncherLight") - 20
                        ColorHueTopbarDelta = Settings.Get(Of Integer)("UiLauncherDelta") - 90
                    Case Else
                        ColorHue = 210 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
                End Select
            End If

            IsDark = Settings.Get(Of Boolean)("UiDarkMode")

            '==== 浅色基准（按主题 HSL 生成）====
            Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.2, 25 + ColorLightAdjust * 0.3)
            Color2 = New MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust)
            Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust)
            Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust)
            Color5 = New MyColor().FromHSL2(ColorHue, ColorSat, 80 + ColorLightAdjust * 0.4)
            Color6 = New MyColor().FromHSL2(ColorHue, ColorSat, 91 + ColorLightAdjust * 0.1)
            Color7 = New MyColor().FromHSL2(ColorHue, ColorSat, 95)
            Color8 = New MyColor().FromHSL2(ColorHue, ColorSat, 97)
            ColorBg0 = Color4 * 0.4 + Color5 * 0.4 + ColorGray4 * 0.2
            ColorBg1 = New MyColor(190, Color7)

            ColorSemiTransparent = New MyColor(1, Color8)

            '表面色（卡片 / 侧栏 / 控件底），深色模式下换成深色
            Dim CardColor As New MyColor(245, 255, 255, 255)
            Dim SidebarColor As New MyColor(241, 255, 255, 255)
            Dim HalfWhiteColor As New MyColor(85, 255, 255, 255)
            Dim SemiWhiteColor As New MyColor(187, 255, 255, 255)
            Dim ShadowColor As New MyColor(52, 61, 74)
            Dim RedBackColor As New MyColor(128, 251, 221, 221)
            '玻璃拟态专用色：标题栏 / 导航栏 / 描边 / 悬浮高光 / 背景光晕
            Dim GlassTitleColor As New MyColor(228, 255, 255, 255)
            Dim GlassNavColor As New MyColor(186, 255, 255, 255)
            Dim GlassBorderColor As New MyColor(20, 0, 0, 0)
            Dim GlassBorderStrongColor As New MyColor(34, 0, 0, 0)
            Dim GlassHoverColor As New MyColor(14, 0, 0, 0)
            Dim CardBorderColor As New MyColor(16, 0, 0, 0)
            Dim GlowColor As New MyColor(56, Color3)

            '深色模式：整体反转明度，保留主题色相。
            'Color1~5 主要用作文字 / 描边 / 图标，需要够亮；Color6~8 是面板底色，需要够暗。
            If IsDark Then
                Dim DarkA As Double = ColorLightAdjust * 0.3
                Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.45, 90 + DarkA)
                Color2 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.95, 72 + DarkA)
                Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 66 + DarkA)
                Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 58 + DarkA)
                Color5 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.9, 46 + DarkA)
                Color6 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.5, 34 + DarkA)
                Color7 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.5, 27 + DarkA)
                Color8 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.45, 21 + DarkA)
                ColorBg0 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.9, 60 + DarkA)
                ColorBg1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.55, 25 + DarkA)
                ColorSemiTransparent = New MyColor(1, Color8)
                '灰度阶：Gray1 最亮（正文），Gray8 最暗（面板）
                ColorGray1 = New MyColor().FromHSL2(ColorHue, 10, 92)
                ColorGray2 = New MyColor().FromHSL2(ColorHue, 10, 80)
                ColorGray3 = New MyColor().FromHSL2(ColorHue, 10, 66)
                ColorGray4 = New MyColor().FromHSL2(ColorHue, 8, 52)
                ColorGray5 = New MyColor().FromHSL2(ColorHue, 8, 41)
                ColorGray6 = New MyColor().FromHSL2(ColorHue, 12, 30)
                ColorGray7 = New MyColor().FromHSL2(ColorHue, 14, 24)
                ColorGray8 = New MyColor().FromHSL2(ColorHue, 16, 18)
                '表面：深色卡片 / 侧栏，保留一点主题色倾向；卡片比侧栏亮一档，侧栏比页面背景亮一档，形成层次
                CardColor = New MyColor(247, New MyColor().FromHSL2(ColorHue, ColorSat * 0.32, 23 + DarkA))
                SidebarColor = New MyColor(240, New MyColor().FromHSL2(ColorHue, ColorSat * 0.35, 18 + DarkA))
                HalfWhiteColor = New MyColor(20, 255, 255, 255)
                SemiWhiteColor = New MyColor(56, 255, 255, 255)
                ShadowColor = New MyColor(0, 0, 0)
                RedBackColor = New MyColor(70, 251, 90, 90)
                '深色玻璃：比页面底色亮一档，靠"亮度差"而不是"白色叠透"来分层
                GlassTitleColor = New MyColor(214, New MyColor().FromHSL2(ColorHue, ColorSat * 0.3, 16 + DarkA))
                GlassNavColor = New MyColor(206, New MyColor().FromHSL2(ColorHue, ColorSat * 0.34, 21 + DarkA))
                GlassBorderColor = New MyColor(26, 255, 255, 255)
                GlassBorderStrongColor = New MyColor(44, 255, 255, 255)
                GlassHoverColor = New MyColor(18, 255, 255, 255)
                CardBorderColor = New MyColor(26, 255, 255, 255)
                GlowColor = New MyColor(46, Color4)
            End If

            Application.Current.Resources("ColorBrush1") = New SolidColorBrush(Color1)
            Application.Current.Resources("ColorBrush2") = New SolidColorBrush(Color2)
            Application.Current.Resources("ColorBrush3") = New SolidColorBrush(Color3)
            Application.Current.Resources("ColorBrush4") = New SolidColorBrush(Color4)
            Application.Current.Resources("ColorBrush5") = New SolidColorBrush(Color5)
            Application.Current.Resources("ColorBrush6") = New SolidColorBrush(Color6)
            Application.Current.Resources("ColorBrush7") = New SolidColorBrush(Color7)
            Application.Current.Resources("ColorBrush8") = New SolidColorBrush(Color8)
            Application.Current.Resources("ColorBrushBg0") = New SolidColorBrush(ColorBg0)
            Application.Current.Resources("ColorBrushBg1") = New SolidColorBrush(ColorBg1)
            Application.Current.Resources("ColorObject1") = CType(Color1, Color)
            Application.Current.Resources("ColorObject2") = CType(Color2, Color)
            Application.Current.Resources("ColorObject3") = CType(Color3, Color)
            Application.Current.Resources("ColorObject4") = CType(Color4, Color)
            Application.Current.Resources("ColorObject5") = CType(Color5, Color)
            Application.Current.Resources("ColorObject6") = CType(Color6, Color)
            Application.Current.Resources("ColorObject7") = CType(Color7, Color)
            Application.Current.Resources("ColorObject8") = CType(Color8, Color)
            Application.Current.Resources("ColorObjectBg0") = CType(ColorBg0, Color)
            Application.Current.Resources("ColorObjectBg1") = CType(ColorBg1, Color)
            '灰度阶（文字/描边/底色）同样随主题推送，深色模式下整体反转
            Application.Current.Resources("ColorBrushGray1") = New SolidColorBrush(ColorGray1)
            Application.Current.Resources("ColorBrushGray2") = New SolidColorBrush(ColorGray2)
            Application.Current.Resources("ColorBrushGray3") = New SolidColorBrush(ColorGray3)
            Application.Current.Resources("ColorBrushGray4") = New SolidColorBrush(ColorGray4)
            Application.Current.Resources("ColorBrushGray5") = New SolidColorBrush(ColorGray5)
            Application.Current.Resources("ColorBrushGray6") = New SolidColorBrush(ColorGray6)
            Application.Current.Resources("ColorBrushGray7") = New SolidColorBrush(ColorGray7)
            Application.Current.Resources("ColorBrushGray8") = New SolidColorBrush(ColorGray8)
            Application.Current.Resources("ColorObjectGray1") = CType(ColorGray1, Color)
            Application.Current.Resources("ColorObjectGray2") = CType(ColorGray2, Color)
            Application.Current.Resources("ColorObjectGray3") = CType(ColorGray3, Color)
            Application.Current.Resources("ColorObjectGray4") = CType(ColorGray4, Color)
            Application.Current.Resources("ColorObjectGray5") = CType(ColorGray5, Color)
            Application.Current.Resources("ColorObjectGray6") = CType(ColorGray6, Color)
            Application.Current.Resources("ColorObjectGray7") = CType(ColorGray7, Color)
            Application.Current.Resources("ColorObjectGray8") = CType(ColorGray8, Color)
            '表面色：卡片 / 侧栏 / 控件底 / 阴影（深色模式下换成深色，保证夜间所有容器一起变暗）
            Application.Current.Resources("ColorBrushCard") = New SolidColorBrush(CardColor)
            Application.Current.Resources("ColorObjectCard") = CType(CardColor, Color)
            Application.Current.Resources("ColorBrushBackgroundTransparentSidebar") = New SolidColorBrush(SidebarColor)
            Application.Current.Resources("ColorBrushHalfWhite") = New SolidColorBrush(HalfWhiteColor)
            Application.Current.Resources("ColorBrushSemiWhite") = New SolidColorBrush(SemiWhiteColor)
            Application.Current.Resources("ColorObjectShadow") = CType(ShadowColor, Color)
            Application.Current.Resources("ColorBrushRedBack") = New SolidColorBrush(RedBackColor)
            '玻璃拟态：标题栏 / 导航栏 / 描边 / 悬浮 / 光晕
            Application.Current.Resources("ColorBrushGlassTitle") = New SolidColorBrush(GlassTitleColor)
            Application.Current.Resources("ColorBrushGlassNav") = New SolidColorBrush(GlassNavColor)
            Application.Current.Resources("ColorBrushGlassBorder") = New SolidColorBrush(GlassBorderColor)
            Application.Current.Resources("ColorBrushGlassBorderStrong") = New SolidColorBrush(GlassBorderStrongColor)
            Application.Current.Resources("ColorBrushGlassHover") = New SolidColorBrush(GlassHoverColor)
            Application.Current.Resources("ColorBrushCardBorder") = New SolidColorBrush(CardBorderColor)
            Application.Current.Resources("ColorBrushNavActive") = New SolidColorBrush(If(IsDark, New MyColor(52, Color4), New MyColor(36, Color3)))
            Application.Current.Resources("ColorObjectGlow") = CType(GlowColor, Color)
            Application.Current.Resources("ColorBrushGlow") = New SolidColorBrush(GlowColor)
            ThemeRefreshMain()
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub
    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If Not FrmMain.IsLoaded Then Return
            '顶部条背景：玻璃拟态，半透明磨砂 + 极淡主题色倾向，让底层背景能微透上来
            Dim Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
            Dim Deltas = ColorHueTopbarDelta.Switch(Function(d) New Integer() {-d, 0, d}, Function(d) d)
            If IsDark Then
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor(203, New MyColor().FromHSL2(ColorHue + Deltas(0), ColorSat * 0.45, 19 + ColorLightAdjust * 0.3))})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor(196, New MyColor().FromHSL2(ColorHue + Deltas(1), ColorSat * 0.40, 22 + ColorLightAdjust * 0.3))})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor(203, New MyColor().FromHSL2(ColorHue + Deltas(2), ColorSat * 0.45, 19 + ColorLightAdjust * 0.3))})
            Else
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor(208, New MyColor().FromHSL2(ColorHue + Deltas(0), ColorSat * 0.70, 99))})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor(200, New MyColor().FromHSL2(ColorHue + Deltas(1), ColorSat * 0.60, 98))})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor(208, New MyColor().FromHSL2(ColorHue + Deltas(2), ColorSat * 0.70, 99))})
            End If
            FrmMain.PanTitle.Background = Brush
            FrmMain.PanTitle.Background.Freeze()
            '背景光晕：两团跟随主题色的极淡径向光，位于内容层之下，制造深度
            Try
                Dim GlowA As Color = CType(If(IsDark, New MyColor(58, Color4), New MyColor(74, Color4)), Color)
                Dim GlowB As Color = CType(If(IsDark, New MyColor(44, Color2), New MyColor(52, Color3)), Color)
                Dim FadeA As Color = CType(New MyColor(0, Color4), Color)
                Dim FadeB As Color = CType(New MyColor(0, Color2), Color)
                FrmMain.ShapeGlowA.Fill = New RadialGradientBrush(New GradientStopCollection From {
                    New GradientStop(GlowA, 0), New GradientStop(FadeA, 1)})
                FrmMain.ShapeGlowB.Fill = New RadialGradientBrush(New GradientStopCollection From {
                    New GradientStop(GlowB, 0), New GradientStop(FadeB, 1)})
            Catch ex As Exception
                Logger.Warn(ex, "刷新背景光晕失败")
            End Try
            '主页面背景
            If Settings.Get(Of Boolean)("UiBackgroundColorful") Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                If IsDark Then
                    Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.6, 12)})
                    Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.6, 14)})
                    Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.6, 12)})
                Else
                    Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8, 91)})
                    Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.8, 91)})
                    Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8, 91)})
                End If
                FrmMain.PanForm.Background = Brush
            Else
                FrmMain.PanForm.Background = If(IsDark, New MyColor().FromHSL2(ColorHue, ColorSat * 0.45, 13), New MyColor(245, 245, 245))
            End If
            FrmMain.PanForm.Background.Freeze()
        End Sub)
    End Sub
    Friend Sub ThemeCheckAll(EffectSetup As Boolean)
        '本开源分支已移除赞助等级校验（ThemeCheckOne 恒返回 True）。
        '原版此处依据赞助等级把各隐藏/赞助主题设为 IsEnabled=False，这里统一解锁。
        If FrmSetupUI Is Nothing Then Return
        With FrmSetupUI
            '玄素黑：显示真实控件，隐藏灰色覆盖层与解锁蒙版
            .RadioLauncherTheme5.IsEnabled = True
            .RadioLauncherTheme5.Opacity = 1
            .RadioLauncherTheme5Gray.Opacity = 0
            .LabLauncherTheme5Unlock.Visibility = Visibility.Collapsed
            '其余隐藏 / 赞助主题全部解锁
            .RadioLauncherTheme6.IsEnabled = True
            .RadioLauncherTheme7.IsEnabled = True
            .RadioLauncherTheme8.IsEnabled = True
            .LabLauncherTheme8Copy.Visibility = Visibility.Collapsed
            .RadioLauncherTheme9.IsEnabled = True
            .LabLauncherTheme9Copy.Visibility = Visibility.Collapsed
            .RadioLauncherTheme10.IsEnabled = True
            .RadioLauncherTheme11.IsEnabled = True
            .LabLauncherTheme11Click.Visibility = Visibility.Collapsed
            .RadioLauncherTheme12.IsEnabled = True
            .RadioLauncherTheme13.IsEnabled = True
            .RadioLauncherTheme14.IsEnabled = True
            '隐藏“赞助解锁主题”横幅
            .PanLauncherHide.Visibility = Visibility.Collapsed
        End With
    End Sub
    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        Return True
    End Function
    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        Return False
    End Function

#End Region

#Region "联网配置"

    ''' <summary>
    ''' 联网获取的配置信息。
    ''' 若获取失败或仍在获取中，可能为 Nothing。
    ''' </summary>
    Public ServerConfig As JObject

    Public ServerLoader As New LoaderTask(Of Integer, Integer)("PCL 配置更新", Sub() Logger.Info("该版本中不包含更新通知功能……"), Priority:=ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1000 * 60 * 60} '超时 1 小时

#End Region

#Region "赞助等级"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return DonationRank.None
        End Get
    End Property

    Public Sub InputPotatoCode(IsUpdating As Boolean)
    End Sub
    Friend Sub GeneratePotatoCode()
    End Sub

    ''' <summary>
    ''' 获取设备识别码。
    ''' </summary>
    Friend Function GetIdentify() As String
        Return "0000-0000-0000-0000"
    End Function

#End Region

End Module
