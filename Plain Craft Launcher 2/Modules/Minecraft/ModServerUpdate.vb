Imports System.Globalization
Imports System.Management
Imports System.Security.Cryptography
Imports System.Text.RegularExpressions

Public Module ModServerUpdate

    Private Const ServerVersionUrl As String = "http://zx8673.ziyoutiandi.cn/version.txt"
    Private Const LocalVersionFileName As String = "ServerUpdateVersion.txt"
    Private Const UpdateMutexName As String = "ZiyouTiandi_ServerUpdateMutex"

    Private Class ServerUpdateInfo
        Public InitialVersion As String
        Public LiveVersion As String
        Public LanzouUrl As String
        Public LanzouPassword As String
    End Class

    Private Class LanzouFileEntry
        Public Id As String
        Public Name As String
    End Class

    Private Class PatchInstallEntry
        Public Version As String
        Public Name As String
        Public ZipPath As String
    End Class

    Private Class PatchBackupEntry
        Public TargetPath As String
        Public BackupPath As String
        Public HadOriginal As Boolean
        Public WasDirectory As Boolean
    End Class

    Private Class PatchManifestFile
        Public RelativePath As String
        Public Sha256 As String
        Public Size As Long = -1
    End Class

    Private Class PatchManifest
        Public Version As String
        Public Files As New List(Of PatchManifestFile)
        Public Deletes As New List(Of String)
    End Class

    Public Sub ServerUpdateCheck(Task As LoaderTask(Of Integer, Integer))
        If CurrentLaunchOptions?.SaveBatch IsNot Nothing Then
            McLaunchLog("导出启动脚本时跳过第三方服务器更新")
            Return
        End If

        Dim UpdateMutex As Mutex = Nothing
        Dim OwnsUpdateMutex As Boolean = False
        Try
            Dim MutexCreatedNew As Boolean
            UpdateMutex = New Mutex(True, UpdateMutexName, MutexCreatedNew)
            If Not MutexCreatedNew Then Throw New Exception("$服务器更新正在运行，请等待另一个启动器完成更新后再启动。")
            OwnsUpdateMutex = True

            ServerUpdateCheckInner(Task)
        Finally
            If UpdateMutex IsNot Nothing Then
                Try
                    If OwnsUpdateMutex Then UpdateMutex.ReleaseMutex()
                Catch ex As Exception
                    Logger.Warn(ex, "释放服务器更新互斥锁失败")
                Finally
                    UpdateMutex.Dispose()
                End Try
            End If
        End Try
    End Sub

    Private Sub ServerUpdateCheckInner(Task As LoaderTask(Of Integer, Integer))
        If IsSelectedGameRunning() Then Throw New Exception("$检测到当前游戏目录已有 Java 游戏进程正在运行，请关闭游戏后再启动。")

        McLaunchLog("开始检查第三方服务器更新")
        Dim UpdateInfo = GetServerUpdateInfo()
        Dim LocalVersion = GetLocalVersion()
        If CompareServerVersion(LocalVersion, UpdateInfo.InitialVersion) < 0 Then
            Throw New Exception("$客户端过旧，请重新下载完整包。")
        End If
        McLaunchLog($"服务器更新版本：本地 {LocalVersion}，初始 {UpdateInfo.InitialVersion}，最新 {UpdateInfo.LiveVersion}")

        Dim VersionsToUpdate = GetVersionsToUpdate(LocalVersion, UpdateInfo.LiveVersion)
        If Not VersionsToUpdate.Any() Then
            McLaunchLog("第三方服务器更新检查完成，无需更新")
            Task.Progress = 1
            Return
        End If

        Dim PatchMap = GetPatchMap(UpdateInfo.LanzouUrl, UpdateInfo.LanzouPassword)
        For Each Version In VersionsToUpdate
            If Not PatchMap.ContainsKey(Version) Then McLaunchLog($"未找到版本 {Version} 的服务器补丁，按配置跳过该版本。", LogLevel.Warn)
        Next
        If Not PatchMap.ContainsKey(UpdateInfo.LiveVersion) Then Throw New Exception($"$未找到最新版本 {UpdateInfo.LiveVersion} 的服务器补丁，无法完成强制更新。")

        Dim PatchesToInstall = VersionsToUpdate.
            Where(Function(Version) PatchMap.ContainsKey(Version)).
            Select(Function(Version) New With {.Version = Version, .Patch = PatchMap(Version)}).
            OrderBy(Function(Entry) VersionToIndex(Entry.Version)).
            ToList()
        If Not PatchesToInstall.Any() Then Throw New Exception("$没有找到任何需要安装的服务器补丁。")

        Dim TargetFolder = PathExeFolder
        DirectoryUtils.Create(TargetFolder)
        DirectoryUtils.Create(PathExeFolder & "PCL\")

        Dim WorkFolder = PathTemp & "ServerUpdate\" & GetUuid() & "\"
        Dim DownloadFolder = WorkFolder & "Download\"
        Dim BackupFolder = WorkFolder & "Backup\"
        DirectoryUtils.Create(DownloadFolder)
        DirectoryUtils.Create(BackupFolder)

        Dim DownloadedPatches As New List(Of PatchInstallEntry)
        Try
            McLaunchLog($"需要安装的服务器补丁：{PatchesToInstall.Select(Function(p) p.Patch.Name).Join(", ")}")
            For i = 0 To PatchesToInstall.Count - 1
                If Task.IsInterrupted Then Return
                Dim PatchInfo = PatchesToInstall(i)
                Dim Patch = PatchInfo.Patch
                Dim TempZip = DownloadFolder & PatchInfo.Version & ".zip"
                McLaunchLog($"开始下载服务器补丁：{Patch.Name}")
                Task.Progress = i / Math.Max(PatchesToInstall.Count * 3, 1)

                Dim RealUrl = LanzouResolveDownloadUrl(Patch.Id)
                NetDownloadByLoader(RealUrl, TempZip, Task, New FileChecker(MinSize:=1), True)
                ValidatePatchZip(TempZip, TargetFolder, PatchInfo.Version)
                DownloadedPatches.Add(New PatchInstallEntry With {.Version = PatchInfo.Version, .Name = Patch.Name, .ZipPath = TempZip})
                McLaunchLog($"服务器补丁下载完成：{Patch.Name}")
            Next

            Dim BackupEntries As New List(Of PatchBackupEntry)
            Try
                McLaunchLog("开始安装服务器补丁")
                InstallPatchArchives(DownloadedPatches, TargetFolder, BackupFolder, BackupEntries, Task)
                SaveLocalVersion(UpdateInfo.LiveVersion)
                RefreshHomepageAfterServerUpdate()
                McLaunchLog($"服务器补丁安装完成，本地版本已更新为 {UpdateInfo.LiveVersion}")
            Catch ex As Exception
                Try
                    RollbackInstalledFiles(BackupEntries)
                    McLaunchLog("服务器补丁安装失败，已回滚已写入文件")
                Catch rollbackEx As Exception
                    Logger.Error(rollbackEx, "回滚服务器补丁失败")
                End Try
                Throw New Exception($"$安装服务器补丁失败，本地版本号保持不变：{ex.GetDisplay(False)}", ex)
            End Try
        Catch ex As Exception
            If ex.Message.StartsWithF("$") Then Throw
            Throw New Exception($"$服务器更新失败：{ex.GetDisplay(False)}", ex)
        Finally
            Try
                DirectoryUtils.Delete(WorkFolder)
            Catch ex As Exception
                Logger.Warn(ex, "清理服务器更新临时目录失败")
            End Try
        End Try

        Task.Progress = 1
        McLaunchLog("第三方服务器更新完成")
    End Sub

    Private Function GetServerUpdateInfo() As ServerUpdateInfo
        Dim Raw = NetRequestByClientRetry(ServerVersionUrl, RequireJson:=False, Encoding:=Encoding.UTF8)
        If String.IsNullOrWhiteSpace(Raw) Then Throw New Exception("$读取服务器版本信息失败：返回为空。")

        Dim Info As New ServerUpdateInfo With {
            .InitialVersion = ExtractVersionConfig(Raw, "Initial version"),
            .LiveVersion = ExtractVersionConfig(Raw, "Live version"),
            .LanzouUrl = ExtractConfig(Raw, "lanzou version"),
            .LanzouPassword = ExtractConfig(Raw, "pass")
        }
        If Info.InitialVersion Is Nothing OrElse Info.LiveVersion Is Nothing OrElse Info.LanzouUrl Is Nothing OrElse Info.LanzouPassword Is Nothing Then
            Throw New Exception("$服务器版本信息格式错误，无法读取 Initial version、Live version、lanzou version 或 pass。")
        End If
        ValidateVersion(Info.InitialVersion)
        ValidateVersion(Info.LiveVersion)
        If CompareServerVersion(Info.InitialVersion, Info.LiveVersion) > 0 Then Throw New Exception("$服务器版本信息错误：Initial version 大于 Live version。")
        Return Info
    End Function

    Public Function ServerUpdateGetLocalVersionText() As String
        Return GetLocalVersion()
    End Function

    Public Function ServerUpdateGetLiveVersionText() As String
        Dim Raw = NetRequestByClientRetry(ServerVersionUrl, RequireJson:=False, Encoding:=Encoding.UTF8)
        If String.IsNullOrWhiteSpace(Raw) Then Throw New Exception("读取服务器版本信息失败：返回为空。")
        Dim LiveVersion = ExtractVersionConfig(Raw, "Live version")
        If LiveVersion Is Nothing Then Throw New Exception("服务器版本信息格式错误，无法读取 Live version。")
        ValidateVersion(LiveVersion)
        Return LiveVersion
    End Function

    Private Function ExtractConfig(Raw As String, Key As String) As String
        Dim Pattern = "(?im)^\s*" & Regex.Escape(Key) & "\s*[:：]\s*(.+?)\s*$"
        Dim Result = Raw.RegexSeek(Pattern)
        If Result Is Nothing Then Return Nothing
        Return Result.Substring(Result.IndexOfAny({":"c, "："c}) + 1).Trim()
    End Function

    Private Function ExtractVersionConfig(Raw As String, Key As String) As String
        Dim Result = ExtractConfig(Raw, Key)
        If Result Is Nothing Then Return Nothing
        Return NormalizeVersion(Result)
    End Function

    Private Function GetLocalVersion() As String
        Dim LocalPath = GetLocalVersionPath()
        If Not FileUtils.Exists(LocalPath) Then Throw New Exception("$客户端版本记录缺失，请重新下载完整包。")
        Try
            Dim LocalVersion = NormalizeVersion(FileUtils.ReadAsString(LocalPath).Trim())
            ValidateVersion(LocalVersion)
            Return LocalVersion
        Catch ex As Exception
            Throw New Exception("$客户端版本记录无法读取或格式错误，请重新下载完整包。", ex)
        End Try
    End Function

    Private Function GetLocalVersionPath() As String
        Return PathExeFolder & "PCL\" & LocalVersionFileName
    End Function

    Private Sub SaveLocalVersion(Version As String)
        DirectoryUtils.Create(PathExeFolder & "PCL\")
        FileUtils.Write(GetLocalVersionPath(), NormalizeVersion(Version), Encoding.UTF8)
    End Sub

    Private Sub RefreshHomepageAfterServerUpdate()
        RunInUi(
        Sub()
            Try
                If FrmMain Is Nothing OrElse FrmLaunchRight Is Nothing Then Return
                If FrmMain.PageCurrent.Page <> FormMain.PageType.Launch Then Return
                FrmLaunchRight.ForceRefresh()
                McLaunchLog("已刷新主页服务器版本显示")
            Catch ex As Exception
                Logger.Warn(ex, "刷新主页服务器版本显示失败")
            End Try
        End Sub)
    End Sub

    Private Function GetVersionsToUpdate(LocalVersion As String, LiveVersion As String) As List(Of String)
        Dim LocalIndex = VersionToIndex(LocalVersion)
        Dim LiveIndex = VersionToIndex(LiveVersion)
        If LocalIndex = LiveIndex Then Return New List(Of String)
        If LocalIndex > LiveIndex Then Throw New Exception("$本地服务器补丁版本高于在线版本，客户端版本记录异常。")

        Dim Result As New List(Of String)
        For Index = LocalIndex + 1 To LiveIndex
            Result.Add(IndexToVersion(Index))
        Next
        Return Result
    End Function

    Private Function NormalizeVersion(Version As String) As String
        If Version Is Nothing Then Return Nothing
        Return Version.Trim().Replace(" ", "")
    End Function

    Private Sub ValidateVersion(Version As String)
        If VersionToIndex(Version) < 0 Then Throw New Exception("$服务器补丁版本号格式错误：" & If(Version, "空"))
    End Sub

    Private Function VersionToIndex(Version As String) As Integer
        Version = NormalizeVersion(Version)
        If String.IsNullOrWhiteSpace(Version) Then Return -1
        Dim Match = Regex.Match(Version, "^(\d+)\.(\d)$")
        If Not Match.Success Then Return -1
        Dim Major As Integer
        Dim Minor As Integer
        If Not Integer.TryParse(Match.Groups(1).Value, NumberStyles.None, CultureInfo.InvariantCulture, Major) Then Return -1
        If Not Integer.TryParse(Match.Groups(2).Value, NumberStyles.None, CultureInfo.InvariantCulture, Minor) Then Return -1
        If Major < 0 OrElse Minor < 0 OrElse Minor > 9 OrElse Major > Integer.MaxValue \ 10 Then Return -1
        Return Major * 10 + Minor
    End Function

    Private Function IndexToVersion(Index As Integer) As String
        If Index < 0 Then Throw New Exception("$服务器补丁版本号序号错误：" & Index)
        Return $"{Index \ 10}.{Index Mod 10}"
    End Function

    Private Function CompareServerVersion(Left As String, Right As String) As Integer
        Return VersionToIndex(Left).CompareTo(VersionToIndex(Right))
    End Function

    Private Function GetPatchVersionFromFileName(FileName As String) As String
        Dim Version = RegexGroup(FileName, "^(\d+\.\d)\.zip$", Options:=RegexOptions.IgnoreCase)
        If Version Is Nothing OrElse VersionToIndex(Version) < 0 Then Return ""
        Return NormalizeVersion(Version)
    End Function

    Private Function GetPatchMap(FolderUrl As String, Password As String) As Dictionary(Of String, LanzouFileEntry)
        Dim Result As New Dictionary(Of String, LanzouFileEntry)
        For Each Patch In LanzouGetFolderFiles(FolderUrl, Password)
            Dim Version = GetPatchVersionFromFileName(Patch.Name)
            If Version = "" Then Continue For
            If Result.ContainsKey(Version) Then
                McLaunchLog($"检测到重复的服务器补丁版本 {Version}，已忽略：{Patch.Name}", LogLevel.Warn)
            Else
                Result.Add(Version, Patch)
            End If
        Next
        If Not Result.Any() Then Throw New Exception("$蓝奏云文件夹中没有匹配版本号的服务器补丁。")
        Return Result
    End Function

    Private Function LanzouGetFolderFiles(FolderUrl As String, Password As String) As List(Of LanzouFileEntry)
        Dim Cookies As New CookieContainer
        Dim Page = LanzouGetText(FolderUrl, Nothing, Cookies)
        Dim FileId = RegexGroup(Page, "filemoreajax\.php\?file=(\d+)")
        If FileId Is Nothing Then Throw New Exception("$解析蓝奏云文件夹失败：未找到 filemoreajax.php。")

        Dim PostData = New Dictionary(Of String, String) From {
            {"lx", RequireJsValue(Page, "lx")},
            {"fid", RequireJsValue(Page, "fid")},
            {"uid", RequireJsValue(Page, "uid")},
            {"puid", RequireJsValue(Page, "puid")},
            {"rep", RequireJsValue(Page, "rep")},
            {"t", RequireJsValue(Page, "t")},
            {"k", RequireJsValue(Page, "k")},
            {"up", RequireJsValue(Page, "up")},
            {"ls", RequireJsValue(Page, "ls")},
            {"pwd", Password}
        }

        Dim Result As New List(Of LanzouFileEntry)
        Dim PageIndex = 1
        Do
            PostData("pg") = PageIndex.ToString(CultureInfo.InvariantCulture)
            Dim JsonText = LanzouPostNoRedirect(
                New Uri(New Uri(FolderUrl), "/filemoreajax.php?file=" & FileId).ToString,
                FolderUrl,
                Cookies,
                New FormUrlEncodedContent(PostData))
            Dim Json = GetJson(JsonText)
            Dim State = Json("zt")?.ToString()
            If State = "2" Then Exit Do
            If State <> "1" Then Throw New Exception("$解析蓝奏云文件列表失败：" & If(Json("info")?.ToString(), "未知错误"))

            Dim Data = Json("text")
            For Each FileToken In Data
                Dim FileIdText = FileToken("id")?.ToString()
                Dim FileName = Net.WebUtility.HtmlDecode(FileToken("name_all")?.ToString())
                If FileIdText Is Nothing OrElse FileIdText = "-1" OrElse String.IsNullOrWhiteSpace(FileName) Then Continue For
                Result.Add(New LanzouFileEntry With {.Id = FileIdText, .Name = FileName})
            Next
            If Data.Count < 50 Then Exit Do
            PageIndex += 1
        Loop

        If Not Result.Any() Then Throw New Exception("$蓝奏云文件夹中没有可用补丁。")
        Return Result.OrderBy(Function(f) GetPatchVersionFromFileName(f.Name), New ServerVersionComparer).ToList
    End Function

    Private Function RequireJsValue(Page As String, Name As String) As String
        Dim Value = RegexGroup(Page, "'" & Regex.Escape(Name) & "'\s*:\s*([^,\r\n}]+)")
        If Value Is Nothing Then Value = RegexGroup(Page, "var\s+" & Regex.Escape(Name) & "\s*=\s*([^;\r\n]+)")
        If Value Is Nothing Then Throw New Exception("$解析蓝奏云文件夹失败：缺少参数 " & Name & "。")
        Value = Value.Trim().Trim(","c).Trim()
        If (Value.StartsWith("'") AndAlso Value.EndsWith("'")) OrElse (Value.StartsWith("""") AndAlso Value.EndsWith("""")) Then
            Return Value.Substring(1, Value.Length - 2)
        End If
        If Value.RegexCheck("^\d+$") Then Return Value
        Dim VariableValue = RegexGroup(Page, "var\s+" & Regex.Escape(Value) & "\s*=\s*'([^']+)'")
        If VariableValue IsNot Nothing Then Return VariableValue
        Throw New Exception("$解析蓝奏云文件夹失败：无法解析参数 " & Name & "。")
    End Function

    Private Function LanzouResolveDownloadUrl(FileId As String) As String
        Dim FilePageUrl = "https://lanzout.com/" & FileId
        Dim Cookies As New CookieContainer
        Dim Page = LanzouGetText(FilePageUrl, Nothing, Cookies)
        Dim IframeSrc = RegexGroup(Page, "<iframe\b[^>]*?\ssrc\s*=\s*[""']([^""']+)[""']", Options:=RegexOptions.IgnoreCase Or RegexOptions.Singleline)
        If IframeSrc Is Nothing Then Throw New Exception("$解析蓝奏云文件页失败：未找到 iframe。")
        Dim IframeUrl = New Uri(New Uri(FilePageUrl), IframeSrc).ToString
        Dim Iframe = LanzouGetText(IframeUrl, FilePageUrl, Cookies)

        Dim AjaxFile = RegexGroup(Iframe, "ajaxm\.php\?file=(\d+)")
        Dim Sign = RegexGroup(Iframe, "wp_sign\s*=\s*'([^']+)'")
        Dim AjaxData = RegexGroup(Iframe, "ajaxdata\s*=\s*'([^']+)'")
        Dim Kdns = If(RegexGroup(Iframe, "kdns\s*=\s*(\d+)"), "1")
        Dim TelecomSuffix = If(RegexGroup(Iframe, "var\s+down_1\s*=\s*'([^']*)'"), "")
        If AjaxFile Is Nothing OrElse Sign Is Nothing OrElse AjaxData Is Nothing Then Throw New Exception("$解析蓝奏云下载参数失败。")

        Dim RealUrl = LanzouGetTelecomDownloadUrl(AjaxFile, Sign, AjaxData, Kdns, TelecomSuffix, IframeUrl, Cookies)
        If String.IsNullOrWhiteSpace(RealUrl) Then Throw New Exception("$蓝奏云未返回真实下载地址。")
        Return RealUrl
    End Function

    Private Function LanzouGetTelecomDownloadUrl(AjaxFile As String, Sign As String, AjaxData As String, Kdns As String, TelecomSuffix As String, Referer As String, Cookies As CookieContainer) As String
        Dim PostData = New Dictionary(Of String, String) From {
            {"action", "downprocess"},
            {"websignkey", AjaxData},
            {"signs", AjaxData},
            {"sign", Sign},
            {"websign", ""},
            {"kd", Kdns},
            {"ves", "1"}
        }
        Dim JsonText = LanzouPostNoRedirect(
            New Uri(New Uri(Referer), "/ajaxm.php?file=" & AjaxFile).ToString,
            Referer,
            Cookies,
            New FormUrlEncodedContent(PostData))
        Dim Json = GetJson(JsonText)
        If Json("zt")?.ToString() <> "1" Then Throw New Exception("$蓝奏云 downprocess 失败：" & JsonText)

        Dim Dom = Json("dom")?.ToString()
        If String.IsNullOrWhiteSpace(Dom) OrElse Kdns = "0" Then Dom = "https://slssctm.dmpdmp.com"
        Dim Url = Json("url")?.ToString()
        If String.IsNullOrWhiteSpace(Url) Then Throw New Exception("$蓝奏云 downprocess 未返回 url。")
        If Url.Contains("?SignError") Then Throw New Exception("$蓝奏云 downprocess 返回签名错误，无法获取电信下载地址。")

        Dim TelecomUrl = Dom.TrimEnd("/"c) & "/file/" & Url & TelecomSuffix
        Return LanzouResolveTelecomFinalUrl(TelecomUrl)
    End Function

    Private Function LanzouGetText(Url As String, Referer As String, Cookies As CookieContainer) As String
        If Cookies Is Nothing Then Cookies = New CookieContainer
        For Attempt = 1 To 3
            Using Handler As New HttpClientHandler With {
                .AllowAutoRedirect = True,
                .AutomaticDecompression = DecompressionMethods.Deflate Or DecompressionMethods.GZip,
                .CookieContainer = Cookies
            }
                Using Client As New HttpClient(Handler)
                    Client.Timeout = TimeSpan.FromSeconds(30)
                    Using Request As New HttpRequestMessage(HttpMethod.Get, Url)
                        Request.Headers.TryAddWithoutValidation("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/126.0.0.0 Safari/537.36")
                        Request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")
                        Request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8")
                        Request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache")
                        Request.Headers.TryAddWithoutValidation("Pragma", "no-cache")
                        If Not String.IsNullOrWhiteSpace(Referer) Then Request.Headers.TryAddWithoutValidation("Referer", Referer)
                        Using Response = Client.SendAsync(Request, HttpCompletionOption.ResponseContentRead).GetAwaiter().GetResult()
                            Dim Text = Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                            If Not Response.IsSuccessStatusCode Then Throw New Exception($"蓝奏云请求失败：HTTP {CInt(Response.StatusCode)}，{Url}")
                            If Text.Contains("var arg1=") Then
                                Cookies.Add(New Uri(Url), New Cookie("acw_sc__v2", ComputeAcwScV2(Text), "/"))
                                Thread.Sleep(200)
                                Continue For
                            End If
                            Return Text
                        End Using
                    End Using
                End Using
            End Using
        Next
        Throw New Exception("蓝奏云验证失败，无法获取页面。")
    End Function

    Private Class LanzouHttpResult
        Public StatusCode As Integer
        Public Location As String
        Public Text As String
        Public ContentType As String
    End Class

    Private Function LanzouResolveTelecomFinalUrl(TelecomUrl As String) As String
        Dim Cookies As New CookieContainer
        For Attempt = 1 To 3
            Dim Result = LanzouGetNoRedirect(TelecomUrl, "https://developer3.lanrar.com/", Cookies)
            If Result.StatusCode >= 300 AndAlso Result.StatusCode < 400 AndAlso Not String.IsNullOrWhiteSpace(Result.Location) Then
                Dim FinalUrl = New Uri(New Uri(TelecomUrl), Result.Location).ToString
                If FinalUrl.Contains("?SignError") Then Throw New Exception("$蓝奏云电信下载签名错误。")
                Return FinalUrl
            End If
            If Result.Text IsNot Nothing AndAlso Result.Text.Contains("var arg1=") Then
                Dim CookieValue = ComputeAcwScV2(Result.Text)
                Cookies.Add(New Uri(TelecomUrl), New Cookie("acw_sc__v2", CookieValue, "/"))
                Thread.Sleep(200)
                Continue For
            End If
            If Result.Text IsNot Nothing AndAlso Result.Text.Contains("down_r(") AndAlso Result.Text.Contains("ajax.php") Then
                Return LanzouResolveTelecomVerifyPage(TelecomUrl, Result.Text, Cookies)
            End If
            If Result.StatusCode = 200 AndAlso Result.ContentType IsNot Nothing AndAlso Result.ContentType.Contains("application/") Then Return TelecomUrl
            Throw New Exception($"$蓝奏云电信下载未返回真实下载地址（HTTP {Result.StatusCode}）。")
        Next
        Throw New Exception("$蓝奏云电信下载验证失败，无法获取真实下载地址。")
    End Function

    Private Function LanzouResolveTelecomVerifyPage(TelecomUrl As String, Html As String, Cookies As CookieContainer) As String
        Dim FileValue = RegexGroup(Html, "'file'\s*:\s*'([^']+)'", Options:=RegexOptions.Singleline)
        Dim SignValue = RegexGroup(Html, "'sign'\s*:\s*'([^']+)'", Options:=RegexOptions.Singleline)
        If String.IsNullOrWhiteSpace(FileValue) OrElse String.IsNullOrWhiteSpace(SignValue) Then Throw New Exception("$蓝奏云电信验证页缺少下载参数。")

        Dim VerifyUrl = New Uri(New Uri(TelecomUrl), "ajax.php").ToString
        Dim PostData = New Dictionary(Of String, String) From {
            {"file", FileValue},
            {"el", "2"},
            {"sign", SignValue}
        }
        Thread.Sleep(2500)
        Dim JsonText = LanzouPostNoRedirect(
            VerifyUrl,
            TelecomUrl,
            Cookies,
            New FormUrlEncodedContent(PostData))
        Dim Json = GetJson(JsonText)
        If Json("zt")?.ToString() <> "1" Then Throw New Exception("$蓝奏云电信验证失败：" & If(Json("url")?.ToString(), JsonText))
        Dim RealUrl = Json("url")?.ToString()
        If String.IsNullOrWhiteSpace(RealUrl) OrElse RealUrl.Contains("?SignError") Then Throw New Exception("$蓝奏云电信验证返回签名错误，无法获取真实下载地址。")
        Return New Uri(New Uri(TelecomUrl), RealUrl).ToString
    End Function

    Private Function LanzouGetNoRedirect(Url As String, Referer As String, Cookies As CookieContainer) As LanzouHttpResult
        Using Handler As New HttpClientHandler With {
            .AllowAutoRedirect = False,
            .AutomaticDecompression = DecompressionMethods.Deflate Or DecompressionMethods.GZip,
            .CookieContainer = Cookies
        }
            Using Client As New HttpClient(Handler)
                Client.Timeout = TimeSpan.FromSeconds(30)
                Using Request As New HttpRequestMessage(HttpMethod.Get, Url)
                    Request.Headers.TryAddWithoutValidation("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/126.0.0.0 Safari/537.36")
                    Request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")
                    Request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8")
                    Request.Headers.TryAddWithoutValidation("Referer", Referer)
                    Using Response = Client.SendAsync(Request, HttpCompletionOption.ResponseContentRead).GetAwaiter().GetResult()
                        Dim Result As New LanzouHttpResult With {
                            .StatusCode = CInt(Response.StatusCode),
                            .Location = If(Response.Headers.Location Is Nothing, Nothing, Response.Headers.Location.ToString),
                            .ContentType = If(Response.Content.Headers.ContentType Is Nothing, "", Response.Content.Headers.ContentType.MediaType)
                        }
                        If Not (Result.StatusCode >= 300 AndAlso Result.StatusCode < 400 AndAlso Not String.IsNullOrWhiteSpace(Result.Location)) Then
                            Result.Text = Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                        End If
                        Return Result
                    End Using
                End Using
            End Using
        End Using
    End Function

    Private Function LanzouPostNoRedirect(Url As String, Referer As String, Cookies As CookieContainer, Content As HttpContent) As String
        Using Handler As New HttpClientHandler With {
            .AllowAutoRedirect = False,
            .AutomaticDecompression = DecompressionMethods.Deflate Or DecompressionMethods.GZip,
            .CookieContainer = Cookies
        }
            Using Client As New HttpClient(Handler)
                Client.Timeout = TimeSpan.FromSeconds(30)
                Using Request As New HttpRequestMessage(HttpMethod.Post, Url)
                    Request.Headers.TryAddWithoutValidation("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/126.0.0.0 Safari/537.36")
                    Request.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01")
                    Request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8")
                    Request.Headers.TryAddWithoutValidation("Referer", Referer)
                    Request.Headers.TryAddWithoutValidation("Origin", New Uri(Referer).GetLeftPart(UriPartial.Authority))
                    Request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest")
                    Request.Content = Content
                    Using Response = Client.SendAsync(Request, HttpCompletionOption.ResponseContentRead).GetAwaiter().GetResult()
                        Return Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    End Using
                End Using
            End Using
        End Using
    End Function

    Private Function ComputeAcwScV2(Html As String) As String
        Dim Arg1 = RegexGroup(Html, "var\s+arg1='([^']+)'", Options:=RegexOptions.Singleline Or RegexOptions.IgnoreCase)
        If Arg1 Is Nothing Then Throw New Exception("蓝奏云验证失败：未找到 arg1。")
        Dim ArraySource = RegexGroup(Html, "function\s+a0i\(\)\{var\s+N=\[([^\]]+)\]", Options:=RegexOptions.Singleline Or RegexOptions.IgnoreCase)
        If ArraySource Is Nothing Then Throw New Exception("蓝奏云验证失败：未找到字符串数组。")

        Dim Table As New List(Of String)
        For Each Match As Match In Regex.Matches(ArraySource, "'([^']*)'", RegexOptions.Singleline)
            Table.Add(Match.Groups(1).Value)
        Next
        If Not Table.Any() Then Throw New Exception("蓝奏云验证失败：字符串数组为空。")

        Dim Cache As New Dictionary(Of String, String)
        Dim GetValue =
            Function(HexIndex As Integer) As String
                Dim Index = HexIndex - 251
                If Index < 0 OrElse Index >= Table.Count Then Throw New Exception("蓝奏云验证失败：字符串索引越界。")
                Dim CacheKey = Index & "|" & Table(Index)
                If Not Cache.ContainsKey(CacheKey) Then Cache(CacheKey) = DecodeAcwToken(Table(Index))
                Return Cache(CacheKey)
            End Function

        Dim Rotations = 0
        Do
            Try
                Dim Value =
                    -JsParseInt(GetValue(279)) / 1 * JsParseInt(GetValue(273)) / 2 +
                    -JsParseInt(GetValue(251)) / 3 * JsParseInt(GetValue(270)) / 4 +
                    -JsParseInt(GetValue(257)) / 5 * -JsParseInt(GetValue(253)) / 6 +
                    -JsParseInt(GetValue(258)) / 7 * JsParseInt(GetValue(290)) / 8 +
                    JsParseInt(GetValue(274)) / 9 +
                    JsParseInt(GetValue(285)) / 10 * JsParseInt(GetValue(284)) / 11 +
                    JsParseInt(GetValue(276)) / 12
                If Math.Abs(Value - 483519) < 0.0001 Then Exit Do
            Catch
            End Try

            Table.Add(Table(0))
            Table.RemoveAt(0)
            Cache.Clear()
            Rotations += 1
            If Rotations > 1000 Then Throw New Exception("蓝奏云验证失败：无法还原字符串数组。")
        Loop

        Dim Order = {15, 35, 29, 24, 33, 16, 1, 38, 10, 9, 19, 31, 40, 27, 22, 23, 25, 13, 6, 11, 39, 18, 20, 8, 14, 21, 32, 26, 2, 30, 7, 4, 17, 5, 3, 28, 34, 37, 12, 36}
        Dim Key = GetValue(277)
        Dim Shuffled = Enumerable.Repeat("", Order.Length).ToArray()
        For SourceIndex = 0 To Arg1.Length - 1
            For TargetIndex = 0 To Order.Length - 1
                If Order(TargetIndex) = SourceIndex + 1 Then Shuffled(TargetIndex) = Arg1(SourceIndex)
            Next
        Next

        Dim Joined = Shuffled.Join("")
        Dim Result As New StringBuilder
        For Index = 0 To Math.Min(Joined.Length, Key.Length) - 1 Step 2
            If Index + 2 > Joined.Length OrElse Index + 2 > Key.Length Then Exit For
            Dim Part = Convert.ToString(Convert.ToInt32(Joined.Substring(Index, 2), 16) Xor Convert.ToInt32(Key.Substring(Index, 2), 16), 16)
            If Part.Length = 1 Then Part = "0" & Part
            Result.Append(Part)
        Next
        Return Result.ToString()
    End Function

    Private Function DecodeAcwToken(Value As String) As String
        Dim Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+/="
        Dim RawBytes As New List(Of Byte)
        Dim Q = 0
        Dim R = 0
        For Each CharValue In Value
            Dim S = Alphabet.IndexOf(CharValue)
            If S < 0 Then Continue For
            If Q Mod 4 <> 0 Then
                R = R * 64 + S
            Else
                R = S
            End If
            Dim OldQ = Q
            Q += 1
            If OldQ Mod 4 <> 0 Then
                RawBytes.Add(CByte(255 And (R >> ((-2 * Q) And 6))))
            End If
        Next
        Return Encoding.UTF8.GetString(RawBytes.ToArray())
    End Function

    Private Function JsParseInt(Value As String) As Double
        Dim Match = Regex.Match(Value, "^[\s]*([+-]?[0-9]+)")
        If Not Match.Success Then Throw New Exception("无法按 JavaScript parseInt 解析：" & Value)
        Return Double.Parse(Match.Groups(1).Value, CultureInfo.InvariantCulture)
    End Function

    Private Sub ValidatePatchZip(ZipPath As String, TargetFolder As String, ExpectedVersion As String)
        Try
            Using Archive = FileUtils.OpenZip(ZipPath)
                Dim Manifest = ReadPatchManifest(Archive)
                If NormalizeVersion(Manifest.Version) <> NormalizeVersion(ExpectedVersion) Then
                    Throw New InvalidDataException($"补丁清单版本不匹配：需要 {ExpectedVersion}，实际 {Manifest.Version}")
                End If
                If Not Manifest.Files.Any() AndAlso Not Manifest.Deletes.Any() Then Throw New InvalidDataException("补丁清单为空")

                For Each Entry In Archive.Entries
                    Dim NormalizedEntry = NormalizeManifestPath(Entry.FullName)
                    If String.IsNullOrEmpty(Entry.Name) OrElse NormalizedEntry = "" Then Continue For
                    If NormalizedEntry <> "update.json" AndAlso Not NormalizedEntry.StartsWith("files/", StringComparison.OrdinalIgnoreCase) Then
                        Throw New InvalidDataException($"压缩包内路径越界：{Entry.FullName}")
                    End If
                Next

                For Each FileEntry In Manifest.Files
                    Dim TargetPath = ResolveClientRelativePath(TargetFolder, FileEntry.RelativePath)
                    Dim ZipEntryPath = "files/" & FileEntry.RelativePath
                    Dim ZipEntry = Archive.GetEntry(ZipEntryPath)
                    If ZipEntry Is Nothing Then ZipEntry = Archive.GetEntry(ZipEntryPath.Replace("/"c, "\"c))
                    If ZipEntry Is Nothing Then Throw New InvalidDataException("补丁包缺少文件：" & ZipEntryPath)
                    If FileEntry.Size >= 0 AndAlso ZipEntry.Length <> FileEntry.Size Then Throw New InvalidDataException($"补丁文件大小不匹配：{FileEntry.RelativePath}")
                    If Not String.IsNullOrWhiteSpace(FileEntry.Sha256) Then
                        Dim Hash = ComputeZipEntrySha256(ZipEntry)
                        If Not Hash.Equals(FileEntry.Sha256, StringComparison.OrdinalIgnoreCase) Then Throw New InvalidDataException($"补丁文件 SHA256 不匹配：{FileEntry.RelativePath}")
                    End If
                Next

                For Each DeletePath In Manifest.Deletes
                    ResolveClientRelativePath(TargetFolder, DeletePath)
                Next
            End Using
        Catch ex As Exception
            Throw New Exception("下载到的补丁不是有效压缩包，或包含不安全路径", ex)
        End Try
    End Sub

    Private Function ReadPatchManifest(Archive As ZipArchive) As PatchManifest
        Dim Entry = Archive.GetEntry("update.json")
        If Entry Is Nothing Then Throw New InvalidDataException("补丁包缺少 update.json")
        Dim ManifestText = Entry.Open().ReadString(Encoding.UTF8).TrimStart(ChrW(&HFEFF))
        Dim Json = GetJson(ManifestText)
        Dim Manifest As New PatchManifest With {.Version = NormalizeVersion(Json("version")?.ToString())}
        ValidateVersion(Manifest.Version)
        For Each FileToken In If(Json("files"), New JArray)
            Dim RelativePath = NormalizeManifestPath(FileToken("path")?.ToString())
            If RelativePath = "" Then Throw New InvalidDataException("补丁清单包含空文件路径")
            Manifest.Files.Add(New PatchManifestFile With {
                .RelativePath = RelativePath,
                .Sha256 = If(FileToken("sha256")?.ToString(), ""),
                .Size = If(FileToken("size") Is Nothing, -1, FileToken("size").ToObject(Of Long))
            })
        Next
        For Each DeleteToken In If(Json("delete"), New JArray)
            Dim DeletePath = NormalizeManifestPath(DeleteToken?.ToString())
            If DeletePath <> "" Then Manifest.Deletes.Add(DeletePath)
        Next
        Return Manifest
    End Function

    Private Sub InstallPatchArchives(Patches As List(Of PatchInstallEntry), TargetFolder As String, BackupFolder As String, BackupEntries As List(Of PatchBackupEntry), Task As LoaderTask(Of Integer, Integer))
        Dim TargetRoot = Path.GetFullPath(TargetFolder).TrimEnd("\"c, "/"c) & "\"
        Dim TotalActions = Math.Max(Patches.Sum(Function(Patch)
                                                   Using Archive = FileUtils.OpenZip(Patch.ZipPath)
                                                       Dim Manifest = ReadPatchManifest(Archive)
                                                       Return Manifest.Files.Count + Manifest.Deletes.Count
                                                   End Using
                                               End Function), 1)
        Dim DoneActions = 0

        For Each Patch In Patches
            If Task.IsInterrupted Then Throw New Exception("服务器更新已取消")
            McLaunchLog($"安装服务器补丁：{Patch.Name}")
            Using Archive = FileUtils.OpenZip(Patch.ZipPath)
                Dim Manifest = ReadPatchManifest(Archive)

                For Each DeletePath In Manifest.Deletes
                    If Task.IsInterrupted Then Throw New Exception("服务器更新已取消")
                    Dim TargetPath = ResolveClientRelativePath(TargetRoot, DeletePath)
                    BackupTarget(TargetPath, BackupFolder, BackupEntries)
                    DeleteTargetPath(TargetPath)
                    DoneActions += 1
                    Task.Progress = (Patches.Count * 2 + DoneActions) / Math.Max(Patches.Count * 2 + TotalActions, 1)
                Next

                For Each FileEntry In Manifest.Files
                    If Task.IsInterrupted Then Throw New Exception("服务器更新已取消")
                    Dim TargetPath = ResolveClientRelativePath(TargetRoot, FileEntry.RelativePath)
                    Dim ZipEntryPath = "files/" & FileEntry.RelativePath
                    Dim ZipEntry = Archive.GetEntry(ZipEntryPath)
                    If ZipEntry Is Nothing Then ZipEntry = Archive.GetEntry(ZipEntryPath.Replace("/"c, "\"c))
                    If ZipEntry Is Nothing Then Throw New InvalidDataException("补丁包缺少文件：" & ZipEntryPath)
                    BackupTarget(TargetPath, BackupFolder, BackupEntries)
                    DirectoryUtils.Create(PathUtils.RemoveLastPart(TargetPath))
                    Using Source = ZipEntry.Open()
                        Using Destination = New FileStream(TargetPath, FileMode.Create, FileAccess.Write, FileShare.None)
                            Source.CopyTo(Destination)
                        End Using
                    End Using
                    If Not String.IsNullOrWhiteSpace(FileEntry.Sha256) Then
                        Dim ActualHash = CryptographyUtils.ComputeFileHash(TargetPath, CryptographyUtils.HashMethod.Sha256)
                        If Not ActualHash.Equals(FileEntry.Sha256, StringComparison.OrdinalIgnoreCase) Then Throw New InvalidDataException("写入后 SHA256 校验失败：" & FileEntry.RelativePath)
                    End If
                    DoneActions += 1
                    Task.Progress = (Patches.Count * 2 + DoneActions) / Math.Max(Patches.Count * 2 + TotalActions, 1)
                Next
            End Using
        Next
    End Sub

    Private Sub BackupTarget(TargetPath As String, BackupFolder As String, BackupEntries As List(Of PatchBackupEntry))
        If BackupEntries.Any(Function(Entry) Entry.TargetPath.Equals(TargetPath, StringComparison.OrdinalIgnoreCase)) Then Return
        Dim HadFile = FileUtils.Exists(TargetPath)
        Dim HadDirectory = DirectoryUtils.Exists(TargetPath)
        Dim BackupPath = Path.GetFullPath(Path.Combine(BackupFolder, BackupEntries.Count.ToString(CultureInfo.InvariantCulture)))
        If HadFile Then
            DirectoryUtils.Create(PathUtils.RemoveLastPart(BackupPath))
            FileUtils.Copy(TargetPath, BackupPath)
        ElseIf HadDirectory Then
            DirectoryUtils.Copy(TargetPath, BackupPath)
        End If
        BackupEntries.Add(New PatchBackupEntry With {.TargetPath = TargetPath, .BackupPath = BackupPath, .HadOriginal = HadFile OrElse HadDirectory, .WasDirectory = HadDirectory})
    End Sub

    Private Sub DeleteTargetPath(TargetPath As String)
        If FileUtils.Exists(TargetPath) Then
            FileUtils.Delete(TargetPath)
        ElseIf DirectoryUtils.Exists(TargetPath) Then
            DirectoryUtils.Delete(TargetPath)
        End If
    End Sub

    Private Sub RollbackInstalledFiles(BackupEntries As List(Of PatchBackupEntry))
        For Each Entry In BackupEntries.AsEnumerable().Reverse()
            Try
                If Entry.HadOriginal Then
                    DeleteTargetPath(Entry.TargetPath)
                    If Entry.WasDirectory Then
                        DirectoryUtils.Copy(Entry.BackupPath, Entry.TargetPath)
                    Else
                        DirectoryUtils.Create(PathUtils.RemoveLastPart(Entry.TargetPath))
                        FileUtils.Copy(Entry.BackupPath, Entry.TargetPath)
                    End If
                ElseIf FileUtils.Exists(Entry.TargetPath) Then
                    FileUtils.Delete(Entry.TargetPath)
                ElseIf DirectoryUtils.Exists(Entry.TargetPath) Then
                    DirectoryUtils.Delete(Entry.TargetPath)
                End If
            Catch ex As Exception
                Logger.Warn(ex, "回滚服务器补丁文件失败：" & Entry.TargetPath)
            End Try
        Next
    End Sub

    Private Function ResolveClientRelativePath(RootFolder As String, RelativePath As String) As String
        RelativePath = NormalizeManifestPath(RelativePath)
        If RelativePath = "" Then Throw New InvalidDataException("补丁路径为空")
        If RelativePath.Contains(":") OrElse RelativePath.StartsWith("/") OrElse RelativePath.StartsWith("\") Then Throw New InvalidDataException("补丁路径不是相对路径：" & RelativePath)
        Dim FullPath = Path.GetFullPath(Path.Combine(RootFolder, RelativePath.Replace("/"c, "\"c)))
        If Not IsPathInsideDirectory(FullPath, RootFolder) Then Throw New InvalidDataException("补丁路径越界：" & RelativePath)
        Return FullPath
    End Function

    Private Function NormalizeManifestPath(RelativePath As String) As String
        If RelativePath Is Nothing Then Return ""
        Return RelativePath.Replace("\"c, "/"c).Trim().TrimStart("/"c)
    End Function

    Private Function ComputeZipEntrySha256(Entry As ZipArchiveEntry) As String
        Using Sha = SHA256.Create()
            Using Stream = Entry.Open()
                Return BitConverter.ToString(Sha.ComputeHash(Stream)).Replace("-", "").ToLowerInvariant()
            End Using
        End Using
    End Function

    Private Function IsPathInsideDirectory(PathText As String, RootFolder As String) As Boolean
        Dim Root = Path.GetFullPath(RootFolder).TrimEnd("\"c, "/"c) & "\"
        Dim FullPath = Path.GetFullPath(PathText)
        Return FullPath.StartsWith(Root, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Function IsSelectedGameRunning() As Boolean
        Try
            If McLaunchProcess IsNot Nothing AndAlso Not McLaunchProcess.HasExited Then Return True
        Catch
        End Try

        Dim MatchPaths As New List(Of String)
        AddProcessMatchPath(MatchPaths, McInstanceSelected.PathIndie)
        AddProcessMatchPath(MatchPaths, McInstanceSelected.PathVersion)
        AddProcessMatchPath(MatchPaths, McFolderSelected)

        Try
            Using Searcher As New ManagementObjectSearcher("SELECT ProcessId, Name, CommandLine FROM Win32_Process WHERE Name='java.exe' OR Name='javaw.exe'")
                For Each ProcessInfo As ManagementObject In Searcher.Get()
                    Dim CommandLine = If(ProcessInfo("CommandLine")?.ToString(), "")
                    If String.IsNullOrWhiteSpace(CommandLine) Then Continue For
                    Dim CommandLineCompare = NormalizePathForProcessCompare(CommandLine)
                    If MatchPaths.Any(Function(PathText) CommandLineCompare.Contains(PathText)) Then
                        McLaunchLog("检测到当前游戏目录 Java 进程：" & CommandLine, LogLevel.Warn)
                        Return True
                    End If
                Next
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "检测 Java 游戏进程失败，已跳过精确进程检测")
        End Try
        Return False
    End Function

    Private Sub AddProcessMatchPath(Paths As List(Of String), PathText As String)
        If String.IsNullOrWhiteSpace(PathText) Then Return
        Paths.Add(NormalizePathForProcessCompare(PathText))
        Try
            Paths.Add(NormalizePathForProcessCompare(PathUtils.ToShortPath(PathText)))
        Catch
        End Try
        Paths.RemoveAll(Function(Text) String.IsNullOrWhiteSpace(Text))
        Dim DistinctPaths = Paths.Distinct().ToList()
        Paths.Clear()
        Paths.AddRange(DistinctPaths)
    End Sub

    Private Function NormalizePathForProcessCompare(PathText As String) As String
        If PathText Is Nothing Then Return ""
        Return PathText.Replace("/", "\").Trim().Trim(""""c).TrimEnd("\"c).ToLowerInvariant()
    End Function

    Private Function RegexGroup(Text As String, Pattern As String, Optional GroupIndex As Integer = 1, Optional Options As RegexOptions = RegexOptions.None) As String
        Try
            Dim Match = Regex.Match(Text, Pattern, Options)
            If Not Match.Success OrElse Match.Groups.Count <= GroupIndex Then Return Nothing
            Return Match.Groups(GroupIndex).Value
        Catch ex As Exception
            Logger.Warn(ex, $"正则解析失败（{Pattern}）")
            Return Nothing
        End Try
    End Function

    Private Class ServerVersionComparer
        Implements IComparer(Of String)
        Public Function Compare(x As String, y As String) As Integer Implements IComparer(Of String).Compare
            If x = "" AndAlso y = "" Then Return 0
            If x = "" Then Return 1
            If y = "" Then Return -1
            Return CompareServerVersion(x, y)
        End Function
    End Class

End Module
