Imports Newtonsoft.Json.Linq

''' <summary>
''' 社区资源收藏夹的本地存储与读写。
''' 全部数据保存在设置项 ToolDownloadFavorites 中，格式为一个 JSON 数组。
''' </summary>
Public Module ModCompFavorites

    ''' <summary>
    ''' 一条收藏记录。只保存展示所需的最小信息，详情需要联网重新获取。
    ''' </summary>
    Public Class FavoriteEntry
        ''' <summary>
        ''' 工程 ID。CurseForge 为数字，Modrinth 为乱码字符串。
        ''' </summary>
        Public Property Id As String = ""
        ''' <summary>
        ''' 所属平台，取值同 ResourcePlatforms。
        ''' </summary>
        Public Property Platform As Integer = 0
        Public Property Slug As String = ""
        Public Property RawName As String = ""
        Public Property Description As String = ""
        ''' <summary>
        ''' 工程类型，取值同 ResourceTypes。
        ''' </summary>
        Public Property Types As Integer = 1
        Public Property LogoUrl As String = ""
        Public Property AddTime As Date = Date.Now
    End Class

    Private Const StoredKey As String = "ToolDownloadFavorites"

    ''' <summary>
    ''' 读取全部收藏记录。出错的条目会被忽略。
    ''' </summary>
    Public Function GetAll() As List(Of FavoriteEntry)
        Dim Result As New List(Of FavoriteEntry)
        Dim Raw As String = ""
        Try
            Raw = Settings.Get(Of String)(StoredKey)
        Catch ex As Exception
            Logger.Warn(ex, "读取收藏夹设置失败")
            Return Result
        End Try
        If String.IsNullOrWhiteSpace(Raw) Then Return Result
        Try
            For Each Item As JToken In JArray.Parse(Raw)
                If Item.Type <> JTokenType.Object Then Continue For
                Result.Add(New FavoriteEntry With {
                    .Id = If(Item("Id"), "").ToString,
                    .Platform = Val(If(Item("Platform"), 0).ToString),
                    .Slug = If(Item("Slug"), "").ToString,
                    .RawName = If(Item("RawName"), "").ToString,
                    .Description = If(Item("Description"), "").ToString,
                    .Types = Val(If(Item("Types"), 1).ToString),
                    .LogoUrl = If(Item("LogoUrl"), "").ToString,
                    .AddTime = If(Item("AddTime") Is Nothing, Date.Now, CDate(Item("AddTime").ToString))
                })
            Next
        Catch ex As Exception
            Logger.Warn(ex, "解析收藏夹数据失败")
        End Try
        '去重（以 Id 为键，保留后加入的）
        Return Result.GroupBy(Function(e) e.Id).Select(Function(g) g.Last()).ToList()
    End Function

    ''' <summary>
    ''' 覆盖写入全部收藏记录。
    ''' </summary>
    Public Sub SaveAll(Items As IEnumerable(Of FavoriteEntry))
        Try
            Dim Array As New JArray
            For Each Entry In Items
                Array.Add(New JObject From {
                    {"Id", Entry.Id},
                    {"Platform", Entry.Platform},
                    {"Slug", Entry.Slug},
                    {"RawName", Entry.RawName},
                    {"Description", Entry.Description},
                    {"Types", Entry.Types},
                    {"LogoUrl", Entry.LogoUrl},
                    {"AddTime", Entry.AddTime}
                })
            Next
            Settings.Set(StoredKey, Array.ToString(Newtonsoft.Json.Formatting.None))
        Catch ex As Exception
            Logger.Error(ex, "保存收藏夹失败")
        End Try
    End Sub

    ''' <summary>
    ''' 判断某个工程是否已被收藏。
    ''' </summary>
    Public Function Contains(ProjectId As String) As Boolean
        If String.IsNullOrEmpty(ProjectId) Then Return False
        Return GetAll().Any(Function(e) e.Id = ProjectId)
    End Function

    ''' <summary>
    ''' 收藏一个工程。若已存在则返回 False。
    ''' </summary>
    Public Function Add(Project As ResourceProject) As Boolean
        If Project Is Nothing Then Return False
        Dim Items = GetAll()
        If Items.Any(Function(e) e.Id = Project.Id) Then Return False
        Items.Add(New FavoriteEntry With {
            .Id = Project.Id,
            .Platform = CInt(Project.Platform),
            .Slug = Project.Slug,
            .RawName = Project.RawName,
            .Description = Project.Description,
            .Types = CInt(Project.Types),
            .LogoUrl = If(Project.LogoUrl, ""),
            .AddTime = Date.Now
        })
        SaveAll(Items)
        Logger.Info($"已收藏资源：{Project.RawName}（{Project.Id}）")
        Return True
    End Function

    ''' <summary>
    ''' 取消收藏。若原本就没有则返回 False。
    ''' </summary>
    Public Function Remove(ProjectId As String) As Boolean
        Dim Items = GetAll()
        Dim Count As Integer = Items.Count
        Items = Items.Where(Function(e) e.Id <> ProjectId).ToList()
        If Items.Count = Count Then Return False
        SaveAll(Items)
        Logger.Info($"已取消收藏资源：{ProjectId}")
        Return True
    End Function

    ''' <summary>
    ''' 切换收藏状态。返回切换后是否处于已收藏状态。
    ''' </summary>
    Public Function Toggle(Project As ResourceProject) As Boolean
        If Project Is Nothing Then Return False
        If Contains(Project.Id) Then
            Remove(Project.Id)
            Return False
        Else
            Add(Project)
            Return True
        End If
    End Function

    ''' <summary>
    ''' 将收藏记录还原为工程对象。需要联网，失败时抛出异常。
    ''' </summary>
    Public Function ToProject(Entry As FavoriteEntry) As ResourceProject
        Return ResourceProject.FromProjectId(Entry.Id, CType(Entry.Platform, ResourcePlatforms))
    End Function

    ''' <summary>
    ''' 获取资源类型的中文名。
    ''' </summary>
    Public Function GetTypeName(Types As ResourceTypes) As String
        If Types.HasFlagF(ResourceTypes.ModPack) Then Return "整合包"
        If Types.HasFlagF(ResourceTypes.ResourcePack) Then Return "资源包"
        If Types.HasFlagF(ResourceTypes.Shader) Then Return "光影包"
        If Types.HasFlagF(ResourceTypes.DataPack) Then Return "数据包"
        If Types.HasFlagF(ResourceTypes.Map) Then Return "世界"
        If Types.HasFlagF(ResourceTypes.Mod) Then Return "Mod"
        Return "资源"
    End Function

End Module
