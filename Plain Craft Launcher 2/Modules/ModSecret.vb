'由于包含加解密等安全信息，本文件中的部分代码已被删除

Friend Module ModSecret

    '标注 PCL 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "OpenSource"
    '在开源版的注册表与常规版的注册表隔离，以防数据冲突
    Public Const RegFolder As String = "ZiyouTiandiLauncher"
    '用于微软登录的 ClientId
    Private Const OAuthClientIdFallback As String = "c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb"
    Public OAuthClientId As String = If(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"), OAuthClientIdFallback)
    'CurseForge API Key
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
        If Url.Contains("api.curseforge.com") Then Req.Headers.Add("x-api-key", CurseForgeAPIKey)
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

    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1)
        Try
            If ThemeNow = NewTheme AndAlso NewTheme >= 0 Then Return
            If NewTheme >= 0 Then ThemeNow = NewTheme
            ApplyLauncherThemeValues()

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
            ThemeRefreshMain()
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub
    Private Sub ApplyLauncherThemeValues()
        Select Case ThemeNow
            Case 0 '龙猫蓝
                ColorHue = 210 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
            Case 1 '甜柠青
                ColorHue = 180 : ColorSat = 65 : ColorLightAdjust = -2 : ColorHueTopbarDelta = 12
            Case 2 '小草绿
                ColorHue = 135 : ColorSat = 62 : ColorLightAdjust = -2 : ColorHueTopbarDelta = 10
            Case 3 '菠萝黄
                ColorHue = 48 : ColorSat = 86 : ColorLightAdjust = -6 : ColorHueTopbarDelta = 8
            Case 4 '橡木棕
                ColorHue = 32 : ColorSat = 62 : ColorLightAdjust = -10 : ColorHueTopbarDelta = 6
            Case 5 '玄素黑
                ColorHue = 220 : ColorSat = 18 : ColorLightAdjust = -24 : ColorHueTopbarDelta = 0
            Case 6 '铁杆粉
                ColorHue = 334 : ColorSat = 74 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 10
            Case 7 '神秘紫
                ColorHue = 272 : ColorSat = 72 : ColorLightAdjust = -2 : ColorHueTopbarDelta = 12
            Case 8 '秋仪金
                ColorHue = 42 : ColorSat = 78 : ColorLightAdjust = -4 : ColorHueTopbarDelta = 14
            Case 9 '活跃橙
                ColorHue = 24 : ColorSat = 84 : ColorLightAdjust = -2 : ColorHueTopbarDelta = 12
            Case 10 '跳票红
                ColorHue = 358 : ColorSat = 78 : ColorLightAdjust = -2 : ColorHueTopbarDelta = 10
            Case 11 '极客蓝
                ColorHue = 198 : ColorSat = 88 : ColorLightAdjust = -3 : ColorHueTopbarDelta = 18
            Case 12 '滑稽彩
                ColorHue = 56 : ColorSat = 92 : ColorLightAdjust = 0 : ColorHueTopbarDelta = New Integer() {-45, 0, 55}
            Case 13 '欧皇彩
                ColorHue = 292 : ColorSat = 78 : ColorLightAdjust = 2 : ColorHueTopbarDelta = New Integer() {-70, 0, 72}
            Case 14 '自定义
                ColorHue = Settings.Get(Of Integer)("UiLauncherHue")
                ColorSat = Settings.Get(Of Integer)("UiLauncherSat")
                ColorLightAdjust = Settings.Get(Of Integer)("UiLauncherLight") - 20
                ColorHueTopbarDelta = Settings.Get(Of Integer)("UiLauncherDelta")
            Case Else
                ColorHue = 210 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
        End Select
    End Sub
    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If Not FrmMain.IsLoaded Then Return
            '顶部条背景
            Dim Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
            Dim Deltas = ColorHueTopbarDelta.Switch(Function(d) New Integer() {-d, 0, d}, Function(d) d)
            Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue + Deltas(0), ColorSat, 48 + ColorLightAdjust)})
            Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue + Deltas(1), ColorSat, 54 + ColorLightAdjust)})
            Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue + Deltas(2), ColorSat, 48 + ColorLightAdjust)})
            FrmMain.PanTitle.Background = Brush
            FrmMain.PanTitle.Background.Freeze()
            '主页面背景
            If Settings.Get(Of Boolean)("UiBackgroundColorful") Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8, 91)})
                FrmMain.PanForm.Background = Brush
            Else
                FrmMain.PanForm.Background = New MyColor(245, 245, 245)
            End If
            FrmMain.PanForm.Background.Freeze()
        End Sub)
    End Sub
    Friend Sub ThemeCheckAll(EffectSetup As Boolean)
        Settings.Set("UiLauncherThemeHide", "0|1|2|3|4|5|6|7|8|9|10|11|12|13|14")
        Settings.Set("UiLauncherThemeHide2", "0|1|2|3|4|5|6|7|8|9|10|11|12|13|14")
    End Sub
    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        Return True
    End Function
    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        Return False
    End Function

#End Region

#Region "更新"

    Private Const LauncherUpdateManifestUrl As String = "http://zx8673.ziyoutiandi.cn/%E8%87%AA%E7%94%B1%E5%A4%A9%E5%9C%B0%E5%90%AF%E5%8A%A8%E5%99%A8%E6%9B%B4%E6%96%B0/%E8%87%AA%E7%94%B1%E5%A4%A9%E5%9C%B0%E5%90%AF%E5%8A%A8%E5%99%A8%E6%9B%B4%E6%96%B0%E6%B8%85%E5%8D%95.txt"
    Private ReadOnly LauncherUpdateCachePath As String = PathTemp & "Cache\LauncherUpdate.txt"
    Private IsLauncherUpdateChecking As Boolean = False

    Private Class LauncherUpdateInfo
        Public Property VersionCode As Integer
        Public Property VersionName As String
        Public Property Url As String
        Public Property Sha256 As String
        Public Property Force As Boolean
        Public Property Notes As String
    End Class

    Friend Sub UpdateCheckByButton()
        LauncherUpdateCheckAsync(False)
    End Sub

    Private Sub LauncherUpdateCheckAsync(IsStartupCheck As Boolean)
        If IsLauncherUpdateChecking Then
            If Not IsStartupCheck Then Hint("正在检查启动器更新，请稍候……")
            Return
        End If
        IsLauncherUpdateChecking = True
        RunInNewThread(
        Sub()
            Try
                If Not IsStartupCheck Then Hint("正在检查启动器更新……")
                Dim Info = LauncherUpdateGetManifest(True)
                If Info.VersionCode <= VersionCode Then
                    If Not IsStartupCheck Then Hint("当前已是最新版！", HintType.Green)
                    Return
                End If
                If String.IsNullOrWhiteSpace(Info.Url) Then Throw New Exception("更新清单中没有填写新版启动器下载地址。")
                Dim Message = $"发现启动器新版本：{Info.VersionName}（{Info.VersionCode}）{vbCrLf}" &
                              $"当前版本：{VersionDisplay}（{VersionCode}）" &
                              If(String.IsNullOrWhiteSpace(Info.Notes), "", $"{vbCrLf}{vbCrLf}更新说明：{Info.Notes}") &
                              $"{vbCrLf}{vbCrLf}是否立即下载并更新？"
                Dim Result = MyMsgBox(Message, "启动器更新", "立即更新", If(Info.Force, "", "取消"), IsWarn:=Info.Force)
                If Result <> 1 Then Return
                LauncherUpdateDownloadAndRestart(Info)
            Catch ex As Exception
                Logger.Warn(ex, "启动器更新检查失败", LogBehavior.None)
                If Not IsStartupCheck Then MyMsgBox("启动器更新检查失败：" & vbCrLf & ex.GetDisplay(False), "更新失败", IsWarn:=True)
            Finally
                IsLauncherUpdateChecking = False
            End Try
        End Sub, "Launcher Update", ThreadPriority.BelowNormal)
    End Sub

    Friend IsUpdateWaitingRestart As Boolean = False
    Private LauncherUpdateNewFile As String = Nothing
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
        If String.IsNullOrWhiteSpace(LauncherUpdateNewFile) OrElse Not FileUtils.Exists(LauncherUpdateNewFile) Then Return
        Try
            Dim Arguments = $"--update {Process.GetCurrentProcess.Id} ""{PathExe}"" ""{LauncherUpdateNewFile}"" true"
            StartProcess(New ProcessStartInfo With {.FileName = LauncherUpdateNewFile, .Arguments = Arguments, .UseShellExecute = True, .WorkingDirectory = PathExeFolder})
            IsUpdateWaitingRestart = False
            If TriggerRestartAndByEnd Then
                FrmMain.EndProgram(False)
            Else
                Environment.Exit(ProcessReturnValues.TaskDone)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "启动自更新替换进程失败", LogBehavior.None)
            MyMsgBox("启动自更新替换进程失败：" & vbCrLf & ex.GetDisplay(False), "更新失败", IsWarn:=True)
        End Try
    End Sub
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
        Try
            Logger.Info($"开始替换启动器：{OldFileName} ← {NewFileName}")
            Try
                Dim OldProcess = Process.GetProcessById(ProcessId)
                If Not OldProcess.HasExited AndAlso Not OldProcess.WaitForExit(120000) Then Throw New TimeoutException("旧启动器进程未能在 120 秒内退出。")
            Catch ex As Exception
                Throw New Exception("等待旧启动器进程退出失败，已取消替换。", ex)
            End Try
            If Not FileUtils.Exists(NewFileName) Then Throw New FileNotFoundException("新版启动器文件不存在。", NewFileName)
            DirectoryUtils.Create(PathUtils.RemoveLastPart(OldFileName))
            Dim BackupFile = OldFileName & ".bak"
            Try
                If FileUtils.Exists(OldFileName) Then FileUtils.Copy(OldFileName, BackupFile)
            Catch ex As Exception
                Logger.Warn(ex, "备份旧启动器失败，继续尝试替换")
            End Try
            FileUtils.Copy(NewFileName, OldFileName)
            Try
                FileUtils.Delete(NewFileName)
                FileUtils.Delete(BackupFile)
            Catch ex As Exception
                Logger.Warn(ex, "清理启动器更新临时文件失败")
            End Try
            If TriggerRestart Then StartProcess(New ProcessStartInfo With {.FileName = OldFileName, .UseShellExecute = True, .WorkingDirectory = PathUtils.RemoveLastPart(OldFileName)})
        Catch ex As Exception
            Logger.Warn(ex, "替换启动器失败", LogBehavior.None)
            MsgBox("替换启动器失败：" & vbCrLf & ex.GetDisplay(False), MsgBoxStyle.Critical, "更新失败")
        End Try
    End Sub

    Public Function LauncherUpdateIsNewest() As Boolean?
        Try
            Dim Raw = FileUtils.TryReadAsString(LauncherUpdateCachePath, Encoding.UTF8)
            If String.IsNullOrWhiteSpace(Raw) Then Return Nothing
            Return LauncherUpdateParseManifest(Raw).VersionCode <= VersionCode
        Catch ex As Exception
            Logger.Warn(ex, "确认启动器更新缓存失败")
            Return Nothing
        End Try
    End Function

    Private Function LauncherUpdateGetManifest(ShowLog As Boolean) As LauncherUpdateInfo
        Dim Raw = NetRequestByClientRetry(LauncherUpdateManifestUrl, Encoding:=Encoding.UTF8)
        FileUtils.Write(LauncherUpdateCachePath, Raw, Encoding.UTF8)
        Dim Info = LauncherUpdateParseManifest(Raw)
        If ShowLog Then Logger.Info($"启动器更新清单：{Info.VersionName} ({Info.VersionCode})")
        Return Info
    End Function

    Private Function LauncherUpdateParseManifest(Raw As String) As LauncherUpdateInfo
        If String.IsNullOrWhiteSpace(Raw) Then Throw New Exception("更新清单为空。")
        Dim Values As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For Each Line In Raw.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(vbLf)
            Dim Trimmed = Line.Trim()
            If Trimmed = "" OrElse Trimmed.StartsWith("#") Then Continue For
            Dim EqualIndex = Trimmed.IndexOf("="c)
            If EqualIndex < 0 Then Continue For
            Values(Trimmed.Substring(0, EqualIndex).Trim()) = Trimmed.Substring(EqualIndex + 1).Trim()
        Next
        Dim CodeText As String = Nothing
        Dim Code As Integer
        If Not Values.TryGetValue("versionCode", CodeText) OrElse Not Integer.TryParse(CodeText, Code) Then Throw New Exception("更新清单中的 versionCode 无效。")
        If Code < 0 Then Throw New Exception("更新清单中的 versionCode 不能小于 0。")
        Dim Info As New LauncherUpdateInfo With {
            .VersionCode = Code,
            .VersionName = If(Values.ContainsKey("versionName"), Values("versionName"), ""),
            .Url = If(Values.ContainsKey("url"), Values("url"), ""),
            .Sha256 = If(Values.ContainsKey("sha256"), Values("sha256"), ""),
            .Notes = If(Values.ContainsKey("notes"), Values("notes"), "")
        }
        If String.IsNullOrWhiteSpace(Info.VersionName) Then Info.VersionName = "自由天地启动器 " & Code
        If Values.ContainsKey("force") Then
            Dim ForceUpdate As Boolean
            If Boolean.TryParse(Values("force"), ForceUpdate) Then Info.Force = ForceUpdate
        End If
        If Info.VersionCode > VersionCode Then
            If String.IsNullOrWhiteSpace(Info.Url) Then Throw New Exception("更新清单中缺少 url。")
            If Not Info.Url.StartsWithF("http://", True) AndAlso Not Info.Url.StartsWithF("https://", True) Then Throw New Exception("更新清单中的 url 必须以 http:// 或 https:// 开头。")
            If Info.Sha256 <> "" AndAlso Not Info.Sha256.RegexCheck("^[0-9a-fA-F]{64}$") Then Throw New Exception("更新清单中的 sha256 格式无效。")
        End If
        Return Info
    End Function

    Private Sub LauncherUpdateDownloadAndRestart(Info As LauncherUpdateInfo)
        Dim UpdateFolder = PathTemp & "LauncherUpdate\"
        Dim NewFile = UpdateFolder & "自由天地启动器.new.exe"
        Try
            DirectoryUtils.Delete(UpdateFolder)
        Catch ex As Exception
            Logger.Warn(ex, "清理旧启动器更新缓存失败")
        End Try
        DirectoryUtils.Create(UpdateFolder)
        Hint("正在下载启动器更新……")
        NetDownloadByLoader(Info.Url, NewFile, SimulateBrowserHeaders:=True)
        If Not FileUtils.Exists(NewFile) Then Throw New Exception("新版启动器下载失败，未找到下载文件。")
        If Not String.IsNullOrWhiteSpace(Info.Sha256) Then
            Dim ActualHash = CryptographyUtils.ComputeFileHash(NewFile, CryptographyUtils.HashMethod.Sha256)
            If Not ActualHash.Equals(Info.Sha256, StringComparison.OrdinalIgnoreCase) Then Throw New Exception($"新版启动器 SHA256 不匹配。{vbCrLf}应为：{Info.Sha256}{vbCrLf}实际：{ActualHash}")
        End If
        LauncherUpdateValidateNewFile(NewFile, Info)
        LauncherUpdateNewFile = NewFile
        IsUpdateWaitingRestart = True
        Hint("启动器更新下载完成，正在重启并替换……", HintType.Green)
        UpdateRestart(True)
    End Sub

    Private Sub LauncherUpdateValidateNewFile(NewFile As String, Info As LauncherUpdateInfo)
        Dim TestProcess = StartProcess(New ProcessStartInfo With {.FileName = NewFile, .Arguments = "--version-code", .UseShellExecute = True, .WorkingDirectory = PathUtils.RemoveLastPart(NewFile)})
        If Not TestProcess.WaitForExit(15000) Then
            Try
                TestProcess.Kill()
            Catch
            End Try
            Throw New Exception("新版启动器版本校验超时。")
        End If
        If TestProcess.ExitCode <> Info.VersionCode Then Throw New Exception($"新版启动器内部版本码与更新清单不一致。{vbCrLf}清单版本码：{Info.VersionCode}{vbCrLf}新版启动器版本码：{TestProcess.ExitCode}{vbCrLf}请确认已在源码中同步修改 VersionCode，并重新上传正确的 exe。")
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    ''' 如果不是，则下载一个。
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        '注意：如果要自行实现这个功能，请换用另一个文件路径，以免与官方版本冲突
    End Sub

#End Region

#Region "联网配置"

    ''' <summary>
    ''' 联网获取的配置信息。
    ''' 若获取失败或仍在获取中，可能为 Nothing。
    ''' </summary>
    Public ServerConfig As JObject

    Public ServerLoader As New LoaderTask(Of Integer, Integer)("PCL 配置更新",
    Sub()
        LauncherUpdateCheckAsync(True)
    End Sub, Priority:=ThreadPriority.BelowNormal) With
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
