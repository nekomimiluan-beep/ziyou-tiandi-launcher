Imports System.Text.RegularExpressions

Public Module ModZiyouTiandiOptionalMods

    Private Const ShaderModFileName As String = "[美化类-Iris光影]iris-neoforge-1.8.14-beta.1+mc1.21.1.jar"
    Private Const HorizonModFileName As String = "[美化类-遥远的地平线]DistantHorizons-3.1.2-b-1.21.1-fabric-neoforge.jar"
    Private Const VrModFileName As String = "[内容类-VivecraftVR]vivecraft-1.21.1-1.3.13-neoforge.jar"
    Private Const ShaderDefaultDisabledFlag As String = "ShaderModDefaultDisabled.flag"
    Private Const HorizonDefaultEnabledFlag As String = "HorizonModDefaultEnabled.flag"

    Private Class OptionalModEntry
        Public Key As String
        Public DisplayName As String
        Public FileName As String
        Public EnableWarning As String
    End Class

    Private Function GetOptionalModEntry(Key As String) As OptionalModEntry
        Select Case If(Key, "").Trim.Lower
            Case "shader", "iris", "光影"
                Return New OptionalModEntry With {
                    .Key = "shader",
                    .DisplayName = "光影MOD",
                    .FileName = ShaderModFileName,
                    .EnableWarning = "打开光影需要极高电脑配置，还有可能出现模型显示问题，是否开启？"
                }
            Case "horizon", "distant", "distanthorizons", "地平线"
                Return New OptionalModEntry With {
                    .Key = "horizon",
                    .DisplayName = "地平线MOD",
                    .FileName = HorizonModFileName,
                    .EnableWarning = "地平线MOD是一款很吃显卡的MOD，开启后可能会导致显卡占用极速增加导致复杂场景卡顿，是否开启？"
                }
            Case "vr", "vivecraft", "虚拟现实"
                Return New OptionalModEntry With {
                    .Key = "vr",
                    .DisplayName = "VR支持",
                    .FileName = VrModFileName,
                    .EnableWarning = "开启 VR 支持需要连接对应设备，并可能影响普通模式兼容性，是否开启？"
                }
            Case Else
                Throw New ArgumentException("未知的自由天地 MOD 类型：" & Key)
        End Select
    End Function

    Private Function GetModsFolder() As String
        Return PathExeFolder & ".minecraft\mods\"
    End Function

    Private Function GetEnabledPath(Entry As OptionalModEntry) As String
        Return GetModsFolder() & Entry.FileName
    End Function

    Private Function GetDisabledPath(Entry As OptionalModEntry) As String
        Return GetEnabledPath(Entry) & ".disabled"
    End Function

    Private Function GetOldPath(Entry As OptionalModEntry) As String
        Return GetEnabledPath(Entry) & ".old"
    End Function

    Private Function OptionalModFileExists(Entry As OptionalModEntry) As Boolean
        Return FileUtils.Exists(GetEnabledPath(Entry)) OrElse FileUtils.Exists(GetDisabledPath(Entry)) OrElse FileUtils.Exists(GetOldPath(Entry))
    End Function

    Public Function ZiyouTiandiOptionalModIsEnabled(Key As String) As Boolean
        Return FileUtils.Exists(GetEnabledPath(GetOptionalModEntry(Key)))
    End Function

    Private Function OptionalModStateText(Key As String) As String
        Dim Entry = GetOptionalModEntry(Key)
        Return Entry.DisplayName & "：" & If(FileUtils.Exists(GetEnabledPath(Entry)), "启用", "关闭")
    End Function

    Private Function OptionalModStateColor(Key As String) As String
        Return If(ZiyouTiandiOptionalModIsEnabled(Key), "Green", "Red")
    End Function

    Public Sub ZiyouTiandiEnsureOptionalModDefaults()
        EnsureOptionalModDefault(GetOptionalModEntry("shader"), False, ShaderDefaultDisabledFlag)
        EnsureOptionalModDefault(GetOptionalModEntry("horizon"), True, HorizonDefaultEnabledFlag)
    End Sub

    Private Sub EnsureOptionalModDefault(Entry As OptionalModEntry, ShouldEnable As Boolean, FlagName As String)
        Dim FlagPath = PathExeFolder & "PCL\" & FlagName
        If FileUtils.Exists(FlagPath) OrElse Not OptionalModFileExists(Entry) Then Return
        Try
            If FileUtils.Exists(GetEnabledPath(Entry)) <> ShouldEnable Then SetOptionalModEnabled(Entry, ShouldEnable)
            DirectoryUtils.Create(PathExeFolder & "PCL\")
            FileUtils.Write(FlagPath, Date.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            Logger.Info($"已完成 {Entry.DisplayName} 首次默认{If(ShouldEnable, "启用", "关闭")}检查")
        Catch ex As Exception
            Logger.Warn(ex, $"首次默认{If(ShouldEnable, "启用", "关闭")} {Entry.DisplayName} 失败")
        End Try
    End Sub

    Public Function ZiyouTiandiReplaceOptionalModHomepageButtons(Content As String) As String
        If String.IsNullOrEmpty(Content) Then Return Content
        ZiyouTiandiEnsureOptionalModDefaults()

        Content = ReplaceOptionalModPlaceholders(Content)
        If Content.Contains("切换自由天地Mod") Then Return Content

        Dim ButtonsXaml = BuildOptionalModButtonsXaml()
        Dim Pattern = "(?<buttons><StackPanel\s+Orientation=""Horizontal""\s+HorizontalAlignment=""Right""\s*>[\s\S]*?Text=""点击加群""[\s\S]*?Text=""打开官网""[\s\S]*?</StackPanel>)"
        Dim Result = Regex.Replace(Content, Pattern,
            Function(Match) ButtonsXaml,
            RegexOptions.IgnoreCase)
        Return Result
    End Function

    Private Function ReplaceOptionalModPlaceholders(Content As String) As String
        Return Content.
            Replace("{vr_mod_text}", EscapeUtils.XmlEscape(OptionalModStateText("vr"))).
            Replace("{vr_mod_color}", OptionalModStateColor("vr")).
            Replace("{shader_mod_text}", EscapeUtils.XmlEscape(OptionalModStateText("shader"))).
            Replace("{shader_mod_color}", OptionalModStateColor("shader")).
            Replace("{horizon_mod_text}", EscapeUtils.XmlEscape(OptionalModStateText("horizon"))).
            Replace("{horizon_mod_color}", OptionalModStateColor("horizon"))
    End Function

    Private Function BuildOptionalModButtonsXaml() As String
        Return "<Grid HorizontalAlignment=""Right"" Margin=""0,-72,0,0"">" & vbCrLf &
               "    <Grid.RowDefinitions>" & vbCrLf &
               "        <RowDefinition Height=""38"" />" & vbCrLf &
               "        <RowDefinition Height=""8"" />" & vbCrLf &
               "        <RowDefinition Height=""38"" />" & vbCrLf &
               "    </Grid.RowDefinitions>" & vbCrLf &
               "    <Grid.ColumnDefinitions>" & vbCrLf &
               "        <ColumnDefinition Width=""120"" />" & vbCrLf &
               "        <ColumnDefinition Width=""10"" />" & vbCrLf &
               "        <ColumnDefinition Width=""120"" />" & vbCrLf &
               "        <ColumnDefinition Width=""10"" />" & vbCrLf &
               "        <ColumnDefinition Width=""120"" />" & vbCrLf &
               "    </Grid.ColumnDefinitions>" & vbCrLf &
               "    <local:MyButton Grid.Row=""0"" Grid.Column=""0"" Width=""120"" Height=""38"" Padding=""10,0""" & vbCrLf &
               "                        ColorType=""" & OptionalModStateColor("vr") & """" & vbCrLf &
               "                        Text=""" & EscapeUtils.XmlEscape(OptionalModStateText("vr")) & """" & vbCrLf &
               "                        EventType=""切换自由天地Mod""" & vbCrLf &
               "                        EventData=""vr""" & vbCrLf &
               "                        ToolTip=""切换 VR 支持 MOD 状态"" />" & vbCrLf &
               "    <local:MyButton Grid.Row=""0"" Grid.Column=""2"" Width=""120"" Height=""38"" Padding=""10,0""" & vbCrLf &
               "                        ColorType=""" & OptionalModStateColor("shader") & """" & vbCrLf &
               "                        Text=""" & EscapeUtils.XmlEscape(OptionalModStateText("shader")) & """" & vbCrLf &
               "                        EventType=""切换自由天地Mod""" & vbCrLf &
               "                        EventData=""shader""" & vbCrLf &
               "                        ToolTip=""切换光影 MOD 状态"" />" & vbCrLf &
               "    <local:MyButton Grid.Row=""0"" Grid.Column=""4"" Width=""120"" Height=""38"" Padding=""10,0""" & vbCrLf &
               "                        ColorType=""" & OptionalModStateColor("horizon") & """" & vbCrLf &
               "                        Text=""" & EscapeUtils.XmlEscape(OptionalModStateText("horizon")) & """" & vbCrLf &
               "                        EventType=""切换自由天地Mod""" & vbCrLf &
               "                        EventData=""horizon""" & vbCrLf &
               "                        ToolTip=""切换地平线 MOD 状态"" />" & vbCrLf &
               "    <local:MyButton Grid.Row=""2"" Grid.Column=""2"" Width=""120"" Height=""38"" Padding=""10,0""" & vbCrLf &
               "                        ColorType=""Highlight""" & vbCrLf &
               "                        Text=""点击加群""" & vbCrLf &
               "                        EventType=""打开网页""" & vbCrLf &
               "                        EventData=""https://qm.qq.com/q/KEQDuNaiek""" & vbCrLf &
               "                        ToolTip=""打开 QQ 加群链接"" />" & vbCrLf &
               "    <local:MyButton Grid.Row=""2"" Grid.Column=""4"" Width=""120"" Height=""38"" Padding=""10,0""" & vbCrLf &
               "                        ColorType=""Highlight""" & vbCrLf &
               "                        Text=""打开官网""" & vbCrLf &
               "                        EventType=""打开网页""" & vbCrLf &
               "                        EventData=""https://www.mcziyou.com""" & vbCrLf &
               "                        ToolTip=""打开 www.mcziyou.com"" />" & vbCrLf &
               "</Grid>"
    End Function

    Public Sub ZiyouTiandiToggleOptionalMod(Key As String)
        Dim Entry = GetOptionalModEntry(Key)
        Try
            If Not OptionalModFileExists(Entry) Then
                MyMsgBox("未找到 " & Entry.DisplayName & " 文件：" & vbCrLf & Entry.FileName, "MOD 文件缺失", IsWarn:=True)
                Return
            End If

            Dim ShouldEnable = Not FileUtils.Exists(GetEnabledPath(Entry))
            If ShouldEnable AndAlso MyMsgBox(Entry.EnableWarning, "开启" & Entry.DisplayName, "开启", "取消", IsWarn:=True) <> 1 Then Return

            SetOptionalModEnabled(Entry, ShouldEnable)
            ClearOptionalModCache()
            Hint(Entry.DisplayName & "已" & If(ShouldEnable, "启用", "关闭") & "！", HintType.Green)
            RunInUi(Sub()
                        If FrmLaunchRight IsNot Nothing Then FrmLaunchRight.ForceRefresh()
                    End Sub)
        Catch ex As Exception
            Logger.Error(ex, "切换自由天地 MOD 状态失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Sub SetOptionalModEnabled(Entry As OptionalModEntry, ShouldEnable As Boolean)
        Dim EnabledPath = GetEnabledPath(Entry)
        Dim DisabledPath = GetDisabledPath(Entry)
        Dim OldPath = GetOldPath(Entry)

        Dim SourcePath As String
        Dim TargetPath As String
        If ShouldEnable Then
            SourcePath = If(FileUtils.Exists(DisabledPath), DisabledPath, OldPath)
            TargetPath = EnabledPath
            If FileUtils.Exists(TargetPath) Then Return
            If Not FileUtils.Exists(SourcePath) Then Throw New FileNotFoundException("未找到已关闭的 MOD 文件：" & Entry.FileName)
        Else
            SourcePath = EnabledPath
            TargetPath = If(FileUtils.Exists(OldPath), OldPath, DisabledPath)
            If Not FileUtils.Exists(SourcePath) Then Return
        End If

        If FileUtils.Exists(TargetPath) Then
            If FileUtils.Exists(SourcePath) Then
                FileUtils.Delete(TargetPath)
            Else
                Logger.Warn("自由天地 MOD 状态已被切换：" & Entry.DisplayName)
                Return
            End If
        End If

        FileUtils.Move(SourcePath, TargetPath)
        Logger.Info($"已切换自由天地 MOD 状态：{Entry.DisplayName}，{SourcePath} -> {TargetPath}")
    End Sub

    Private Sub ClearOptionalModCache()
        Try
            FileUtils.Delete(PathTemp & "Cache\LocalMod.json")
        Catch ex As Exception
            Logger.Warn(ex, "清理 MOD 列表缓存失败")
        End Try
    End Sub

End Module
