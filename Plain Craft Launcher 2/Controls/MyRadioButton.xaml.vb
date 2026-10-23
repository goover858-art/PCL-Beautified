Imports System.Windows.Markup

<ContentProperty("Inlines")>
Public Class MyRadioButton

    '基础

    Public Uuid As Integer = GetUuid()
    Public Event Check(sender As Object, raiseByMouse As Boolean)

    '自定义属性

    Public Property Logo As String
        Get
            Return ShapeLogo.Data.ToString
        End Get
        Set(value As String)
            ShapeLogo.Data = (New GeometryConverter).ConvertFromString(value)
        End Set
    End Property
    Private _LogoScale As Double = 1
    Public Property LogoScale() As Double
        Get
            Return _LogoScale
        End Get
        Set(value As Double)
            _LogoScale = value
            If Not IsNothing(ShapeLogo) Then ShapeLogo.RenderTransform = New ScaleTransform With {.ScaleX = LogoScale, .ScaleY = LogoScale}
        End Set
    End Property

    '竖向（侧边栏）形态

    Private _Vertical As Boolean = False
    ''' <summary>
    ''' 是否使用竖向布局：图标在上、文字在下，适合侧边栏导航。
    ''' </summary>
    Public Property Vertical As Boolean
        Get
            Return _Vertical
        End Get
        Set(value As Boolean)
            _Vertical = value
            RefreshLayout()
        End Set
    End Property
    Private Sub RefreshLayout()
        Try
            If ShapeLogo Is Nothing OrElse PanContent Is Nothing Then Return
            If _Vertical Then
                PanBack.Width = 64
                PanBack.MinHeight = 58
                PanBack.MaxHeight = 58
                PanBack.CornerRadius = New CornerRadius(13)
                PanBack.Margin = New Thickness(6, 6, 6, 0)
                PanContent.Orientation = Orientation.Vertical
                ShapeLogo.MaxWidth = 21
                ShapeLogo.MaxHeight = 21
                ShapeLogo.Margin = New Thickness(0, 10, 0, 0)
                ShapeLogo.HorizontalAlignment = HorizontalAlignment.Center
                LabText.Margin = New Thickness(0, 6, 0, 9)
                LabText.FontSize = 10.5
                LabText.HorizontalAlignment = HorizontalAlignment.Center
            Else
                PanBack.Width = Double.NaN
                PanBack.MinHeight = 27
                PanBack.MaxHeight = 27
                PanBack.CornerRadius = New CornerRadius(13.5)
                PanBack.Margin = New Thickness(0)
                PanContent.Orientation = Orientation.Horizontal
                ShapeLogo.MaxWidth = 16
                ShapeLogo.MaxHeight = 16
                ShapeLogo.Margin = New Thickness(12, 0, 0, 0)
                ShapeLogo.HorizontalAlignment = HorizontalAlignment.Left
                LabText.Margin = New Thickness(8, 0, 12, 0)
                LabText.ClearValue(TextBlock.FontSizeProperty)
                LabText.HorizontalAlignment = HorizontalAlignment.Left
            End If
            '布局变化后重新上色（此时不能走动画）
            RefreshColor(Nothing, False)
        Catch ex As Exception
            Logger.Warn(ex, "刷新单选按钮布局出错")
        End Try
    End Sub
    'XAML 里设置 Vertical 时控件尚未初始化完成，需要等 Loaded 后再落地一次
    Private Sub RadioButtonLoaded() Handles Me.Loaded
        If _Vertical Then RefreshLayout()
    End Sub

    Private _Checked As Boolean = False '是否选中
    Public Property Checked As Boolean
        Get
            Return _Checked
        End Get
        Set(value As Boolean)
            SetChecked(value, False, True)
        End Set
    End Property
    ''' <summary>
    ''' 手动设置 Checked 属性。
    ''' </summary>
    ''' <param name="value">新的 Checked 属性。</param>
    ''' <param name="raiseByMouse">是否由用户引发。</param>
    ''' <param name="anime">是否执行动画。</param>
    Public Sub SetChecked(value As Boolean, raiseByMouse As Boolean, anime As Boolean)
        Try

            '自定义属性基础

            Dim IsChanged As Boolean = False
            If _Checked <> value Then
                _Checked = value
                IsChanged = True
            End If

            '保证只有一个单选框选中

            If Parent Is Nothing Then Return
            Dim RadioboxList As New List(Of MyRadioButton)
            Dim CheckedCount As Integer = 0
            '收集控件列表与选中个数
            For Each Control In CType(Parent, Object).Children
                If TypeOf Control Is MyRadioButton Then
                    RadioboxList.Add(Control)
                    If Control.Checked Then CheckedCount += 1
                End If
            Next
            '判断选中情况
            Select Case CheckedCount
                Case 0
                    '没有任何单选框被选中，选择第一个
                    RadioboxList(0).Checked = True
                Case Is > 1
                    '选中项目多于 1 个
                    If Me.Checked Then
                        '如果本控件选中，则取消其他所有控件的选中
                        For Each Control As MyRadioButton In RadioboxList
                            If Control.Checked AndAlso Not Control.Equals(Me) Then Control.Checked = False
                        Next
                    Else
                        '如果本控件未选中，则只保留第一个选中的控件
                        Dim FirstChecked = False
                        For Each Control As MyRadioButton In RadioboxList
                            If Control.Checked Then
                                If FirstChecked Then
                                    Control.Checked = False '修改 Checked 会自动触发 Change 事件，所以不用额外触发
                                Else
                                    FirstChecked = True
                                End If
                            End If
                        Next
                    End If
            End Select

            '更改动画

            If Not IsChanged Then Return
            RefreshColor(Nothing, anime)

            '触发事件
            If Checked Then RaiseEvent Check(Me, raiseByMouse)
            RaiseCustomEvent()

        Catch ex As Exception
            Logger.Error(ex, "单选按钮勾选改变错误", LogBehavior.Toast)
        End Try
    End Sub

    Public ReadOnly Property Inlines As InlineCollection
        Get
            Return LabText.Inlines
        End Get
    End Property
    Public Property Text As String
        Get
            Return GetValue(TextProperty)
        End Get
        Set(value As String)
            SetValue(TextProperty, value)
        End Set
    End Property '内容
    Public Shared ReadOnly TextProperty As DependencyProperty = DependencyProperty.Register("Text", GetType(String), GetType(MyRadioButton), New PropertyMetadata(New PropertyChangedCallback(
    Sub(sender, e) If sender IsNot Nothing Then CType(sender, MyRadioButton).LabText.Text = e.NewValue)))
    Public Enum ColorState
        White
        Highlight
        ''' <summary>
        ''' 侧边栏竖向导航：未选中为灰色文字，选中为主题色 + 淡色胶囊底。
        ''' </summary>
        Sidebar
    End Enum
    Private _ColorType As ColorState = ColorState.White
    Public Property ColorType As ColorState
        Get
            Return _ColorType
        End Get
        Set(value As ColorState)
            _ColorType = value
            RefreshColor()
        End Set
    End Property '颜色类别

    '点击事件

    Public Event PreviewClick(sender As Object, e As RouteEventArgs)
    Private IsMouseDown As Boolean = False
    Private Sub Radiobox_MouseUp() Handles Me.MouseLeftButtonUp
        If Checked Then Return
        If Not IsMouseDown Then Return
        Logger.Info($"按下单选按钮：{Text}")
        IsMouseDown = False
        Dim e As New RouteEventArgs(True)
        RaiseEvent PreviewClick(Me, e)
        If e.Handled Then Return
        SetChecked(True, True, True)
    End Sub
    Private Sub Radiobox_MouseDown() Handles Me.MouseLeftButtonDown
        If Checked Then Return
        IsMouseDown = True
        RefreshColor()
    End Sub
    Private Sub Radiobox_MouseLeave() Handles Me.MouseLeave
        IsMouseDown = False
    End Sub

    '动画

    Private Const AnimationTimeOfMouseIn As Integer = 90 '鼠标指向动画长度
    Private Const AnimationTimeOfMouseOut As Integer = 150 '鼠标移出动画长度
    Private Const AnimationTimeOfCheck As Integer = 120 '勾选状态变更动画长度
    Private Sub RefreshColor(Optional obj = Nothing, Optional e = Nothing) Handles Me.MouseEnter, Me.MouseLeave, Me.Loaded
        Try
            '侧边栏模式不走颜色动画：AaColor 的字符串重载是"增量"语义，
            '和绝对色混用会让 hover 结束后按钮停在亮色上，看起来像一直选中
            If ColorType = ColorState.Sidebar Then
                AniStop("MyRadioButton Checked " & Uuid)
                AniStop("MyRadioButton Color " & Uuid)
                If Checked Then
                    SetResourceReference(BackgroundProperty, "ColorBrushNavActive")
                    ShapeLogo.SetResourceReference(Shapes.Path.FillProperty, "ColorBrush3")
                    LabText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3")
                    If _Vertical Then
                        ShapeIndicator.Visibility = Visibility.Visible
                        ShapeIndicator.SetResourceReference(Border.BackgroundProperty, "ColorBrush3")
                    End If
                ElseIf IsMouseOver Then
                    '指向：只把文字加深，不给主题色。这样悬停和选中在颜色上完全区分得开
                    SetResourceReference(BackgroundProperty, "ColorBrushGlassHover")
                    ShapeLogo.SetResourceReference(Shapes.Path.FillProperty, "ColorBrushGray1")
                    LabText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray1")
                    ShapeIndicator.Visibility = Visibility.Collapsed
                Else
                    Background = New MyColor(0, 0, 0, 0)
                    '常态用 Gray1：Gray2 在浅色模式的玻璃底上太浅，导航文字基本看不清
                    ShapeLogo.SetResourceReference(Shapes.Path.FillProperty, "ColorBrushGray1")
                    LabText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray1")
                    ShapeIndicator.Visibility = Visibility.Collapsed
                End If
                Return
            End If
            If IsLoaded AndAlso AniControlEnabled = 0 AndAlso Not False.Equals(e) Then '防止默认属性变更触发动画，若强制不执行动画，则 e 为 False

                Select Case ColorType
                    Case ColorState.White
                        If Checked Then
                            '勾选
                            AniStart({
                                         AaColor(ShapeLogo, Shapes.Path.FillProperty, "ColorBrush3", AnimationTimeOfCheck),
                                         AaColor(LabText, TextBlock.ForegroundProperty, "ColorBrush3", AnimationTimeOfCheck)
                                    }, "MyRadioButton Checked " & Uuid)
                            AniStart(AaColor(Me, BackgroundProperty, New MyColor(255, 255, 255) - Background, AnimationTimeOfCheck), "MyRadioButton Color " & Uuid)
                        ElseIf IsMouseDown Then
                            '按下
                            AniStart(AaColor(Me, BackgroundProperty, New MyColor(120, Color8) - Background, 60), "MyRadioButton Color " & Uuid)
                        ElseIf IsMouseOver Then
                            '指向
                            AniStart({
                                 AaColor(ShapeLogo, Shapes.Path.FillProperty, New MyColor(255, 255, 255) - ShapeLogo.Fill, AnimationTimeOfMouseIn),
                                 AaColor(LabText, TextBlock.ForegroundProperty, New MyColor(255, 255, 255) - LabText.Foreground, AnimationTimeOfMouseIn)
                            }, "MyRadioButton Checked " & Uuid)
                            AniStart(AaColor(Me, BackgroundProperty, New MyColor(50, Color8) - Background, AnimationTimeOfMouseIn), "MyRadioButton Color " & Uuid)
                        Else
                            '正常
                            AniStart({
                                 AaColor(ShapeLogo, Shapes.Path.FillProperty, New MyColor(255, 255, 255) - ShapeLogo.Fill, AnimationTimeOfMouseOut),
                                 AaColor(LabText, TextBlock.ForegroundProperty, New MyColor(255, 255, 255) - LabText.Foreground, AnimationTimeOfMouseOut)
                            }, "MyRadioButton Checked " & Uuid)
                            AniStart(AaColor(Me, BackgroundProperty, ColorSemiTransparent - Background, AnimationTimeOfMouseOut), "MyRadioButton Color " & Uuid)
                        End If
                    Case ColorState.Highlight
                        If Checked Then
                            '勾选
                            AniStart({
                                         AaColor(ShapeLogo, Shapes.Path.FillProperty, New MyColor(255, 255, 255) - ShapeLogo.Fill, AnimationTimeOfCheck),
                                         AaColor(LabText, TextBlock.ForegroundProperty, New MyColor(255, 255, 255) - LabText.Foreground, AnimationTimeOfCheck)
                                    }, "MyRadioButton Checked " & Uuid)
                            AniStart(AaColor(Me, BackgroundProperty, "ColorBrush3", AnimationTimeOfCheck), "MyRadioButton Color " & Uuid)
                        ElseIf IsMouseDown Then
                            '按下
                            AniStart(AaColor(Me, BackgroundProperty, "ColorBrush6", AnimationTimeOfMouseIn), "MyRadioButton Color " & Uuid)
                        ElseIf IsMouseOver Then
                            '指向
                            AniStart({
                                         AaColor(ShapeLogo, Shapes.Path.FillProperty, "ColorBrush3", AnimationTimeOfMouseIn),
                                         AaColor(LabText, TextBlock.ForegroundProperty, "ColorBrush3", AnimationTimeOfMouseIn)
                                    }, "MyRadioButton Checked " & Uuid)
                            AniStart(AaColor(Me, BackgroundProperty, "ColorBrush7", AnimationTimeOfMouseIn), "MyRadioButton Color " & Uuid)
                        Else
                            '正常
                            AniStart({
                                         AaColor(ShapeLogo, Shapes.Path.FillProperty, "ColorBrush3", AnimationTimeOfMouseOut),
                                         AaColor(LabText, TextBlock.ForegroundProperty, "ColorBrush3", AnimationTimeOfMouseOut)
                                    }, "MyRadioButton Checked " & Uuid)
                            AniStart(AaColor(Me, BackgroundProperty, ColorSemiTransparent - Background, AnimationTimeOfMouseOut), "MyRadioButton Color " & Uuid)
                        End If
                End Select

            Else

                '不使用动画
                AniStop("MyRadioButton Checked " & Uuid)
                AniStop("MyRadioButton Color " & Uuid)
                Select Case ColorType
                    Case ColorState.White
                        If Checked Then
                            Background = New MyColor(255, 255, 255)
                            ShapeLogo.SetResourceReference(Shapes.Path.FillProperty, "ColorBrush3")
                            LabText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3")
                        Else
                            Background = ColorSemiTransparent
                            ShapeLogo.Fill = New MyColor(255, 255, 255)
                            LabText.Foreground = New MyColor(255, 255, 255)
                        End If
                    Case ColorState.Highlight
                        If Checked Then
                            SetResourceReference(BackgroundProperty, "ColorBrush3")
                            ShapeLogo.Fill = New MyColor(255, 255, 255)
                            LabText.Foreground = New MyColor(255, 255, 255)
                        Else
                            Background = ColorSemiTransparent
                            ShapeLogo.SetResourceReference(Shapes.Path.FillProperty, "ColorBrush3")
                            LabText.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush3")
                        End If
                End Select

            End If
        Catch ex As Exception
            Logger.Warn(ex, "刷新单选按钮颜色出错")
        End Try
    End Sub

End Class
