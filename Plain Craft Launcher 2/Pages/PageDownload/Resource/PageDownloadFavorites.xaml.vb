''' <summary>
''' 社区资源收藏夹页面。展示本地收藏的资源，可搜索、取消收藏、点击进入详情。
''' </summary>
Public Class PageDownloadFavorites

    Private Const IconDelete As String =
        "M6,19c0,1.1 0.9,2 2,2h8c1.1,0 2,-0.9 2,-2V7H6v12zM19,4h-3.5l-1,-1h-5l-1,1H5v2h14V4z"

    ''' <summary>
    ''' 当前展示的 (条目, 工程) 缓存，用于点击进入详情时避免重复联网。
    ''' </summary>
    Private ReadOnly ProjectCache As New Dictionary(Of String, ResourceProject)

    ''' <summary>
    ''' 当前搜索关键词。
    ''' </summary>
    Private SearchText As String = ""

#Region "加载与刷新"

    Private Sub Page_Loaded(sender As Object, e As EventArgs) Handles Me.Loaded
        PanLoad.Visibility = Visibility.Collapsed
        RefreshList()
    End Sub

    ''' <summary>
    ''' 重新构建收藏列表。
    ''' </summary>
    Public Sub RefreshList()
        Try
            Dim Entries = ModCompFavorites.GetAll()
            '搜索过滤
            Dim Filtered = Entries
            If Not String.IsNullOrWhiteSpace(SearchText) Then
                Dim Key As String = SearchText.Trim.Lower
                Filtered = Entries.Where(
                    Function(en)
                        Return (en.RawName IsNot Nothing AndAlso en.RawName.Lower.Contains(Key)) OrElse
                               (en.Description IsNot Nothing AndAlso en.Description.Lower.Contains(Key)) OrElse
                               (en.Slug IsNot Nothing AndAlso en.Slug.Lower.Contains(Key))
                    End Function).ToList()
            End If
            '重建 UI
            PanCards.Children.Clear()
            HintEmpty.Visibility = If(Entries.Any(), Visibility.Collapsed, Visibility.Visible)
            If Not Filtered.Any() Then
                If Entries.Any() Then Hint("没有符合条件的收藏", Log:=False)
                Return
            End If
            '按类型分组，顺序固定以便观感稳定
            Dim Order As New List(Of ResourceTypes) From {
                ResourceTypes.Mod, ResourceTypes.ModPack, ResourceTypes.ResourcePack,
                ResourceTypes.Shader, ResourceTypes.DataPack, ResourceTypes.Map}
            For Each Type In Order
                Dim Group = Filtered.Where(Function(en) CType(en.Types, ResourceTypes).HasFlagF(Type)).ToList()
                If Not Group.Any() Then Continue For
                Dim Card As New MyCard With {.Title = $"{ModCompFavorites.GetTypeName(Type)}（{Group.Count}）", .Margin = New Thickness(0, 0, 0, 15)}
                Dim List As New StackPanel With {.Margin = New Thickness(12, 38, 12, 12)}
                For Each Entry In Group
                    List.Children.Add(BuildItem(Entry))
                Next
                Card.Children.Add(List)
                PanCards.Children.Add(Card)
            Next
            Logger.Info($"收藏夹已刷新，共 {Filtered.Count} 项")
        Catch ex As Exception
            Logger.Error(ex, "刷新收藏夹失败")
        End Try
    End Sub

    ''' <summary>
    ''' 为一个收藏条目构建列表项。
    ''' </summary>
    Private Function BuildItem(Entry As ModCompFavorites.FavoriteEntry) As MyListItem
        Dim Text As String = If(Entry.Description, "").ReplaceLineEndings("").Trim
        If Text.Length > 100 Then Text = Text.Substring(0, 100) & "……"
        Dim Item As New MyListItem With {
            .Title = Entry.RawName,
            .Info = Text,
            .Height = 64,
            .Type = MyListItem.CheckType.Clickable,
            .Tag = Entry
        }
        If Not String.IsNullOrEmpty(Entry.LogoUrl) Then Item.Logo = Entry.LogoUrl
        '取消收藏按钮
        Dim BtnDelete As New MyIconButton With {
            .Logo = IconDelete,
            .LogoScale = 0.72,
            .Tag = Entry,
            .ToolTip = "取消收藏"
        }
        AddHandler BtnDelete.Click, AddressOf BtnDelete_Click
        Item.Buttons = New MyIconButton() {BtnDelete}
        AddHandler Item.Click, AddressOf Item_Click
        Return Item
    End Function

    Private Sub OnSearchTextChanged(sender As Object, e As EventArgs) Handles PanSearchBox.TextChanged
        SearchText = PanSearchBox.Text
        RefreshList()
    End Sub

#End Region

#Region "操作"

    ''' <summary>
    ''' 点击列表项：进入资源详情页。
    ''' </summary>
    Private Sub Item_Click(sender As Object, e As MouseButtonEventArgs)
        Dim Item = TryCast(sender, MyListItem)
        If Item Is Nothing Then Return
        Dim Entry = TryCast(Item.Tag, ModCompFavorites.FavoriteEntry)
        If Entry Is Nothing Then Return
        Try
            Dim Project As ResourceProject = Nothing
            If ProjectCache.ContainsKey(Entry.Id) Then
                Project = ProjectCache(Entry.Id)
            Else
                Hint("正在获取资源信息……", Log:=False)
                Project = ModCompFavorites.ToProject(Entry)
                ProjectCache(Entry.Id) = Project
            End If
            Dim Titles As New List(Of String)
            FrmMain.PageChange(New FormMain.PageStackData With {
                .Page = FormMain.PageType.ResourceDetail,
                .Additional = {Project, Titles, Nothing, ModLoaders.None, ResourceTypes.Any}})
        Catch ex As Exception
            Logger.Error(ex, $"从收藏夹打开资源详情失败（{Entry.Id}）")
            Hint($"无法获取该资源的信息，可能已被删除。{vbCrLf}{ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' 取消单个收藏。
    ''' </summary>
    Private Sub BtnDelete_Click(sender As Object, e As EventArgs)
        Dim Btn = TryCast(sender, MyIconButton)
        If Btn Is Nothing Then Return
        Dim Entry = TryCast(Btn.Tag, ModCompFavorites.FavoriteEntry)
        If Entry Is Nothing Then Return
        If ModCompFavorites.Remove(Entry.Id) Then
            ProjectCache.Remove(Entry.Id)
            Hint("已取消收藏", Log:=False)
            RefreshList()
        End If
    End Sub

    ''' <summary>
    ''' 清空全部收藏。
    ''' </summary>
    Private Sub BtnClearAll_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnClearAll.Click
        Dim Count As Integer = ModCompFavorites.GetAll().Count
        If Count = 0 Then
            Hint("收藏夹已经是空的", Log:=False)
            Return
        End If
        If MyMsgBox($"确定要清空收藏夹中的全部 {Count} 个资源吗？", "清空收藏夹", "确定", "取消") <> 1 Then Return
        ModCompFavorites.SaveAll(New List(Of ModCompFavorites.FavoriteEntry))
        ProjectCache.Clear()
        Hint("已清空收藏夹", Log:=False)
        RefreshList()
    End Sub

#End Region

End Class
