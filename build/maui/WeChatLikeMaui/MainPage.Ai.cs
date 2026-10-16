using System.Text.Json;

namespace WeChatLikeMaui;

public partial class MainPage
{
    private sealed record AiProfile(string Id, string Name, string Protocol, string BaseUrl, string Model, string KeyEnvironment, bool HasKey);
    private readonly List<AiProfile> _aiProfiles = [];
    private bool _aiLoaded, _aiBusy, _aiRunning, _aiLoading, _aiDirty, _contactsBusy;
    private string _aiEditingProfileId = "";

    private void InitializeAi()
    {
        AiContactsSelector.SetAutomationPrefix("AiContacts");
        foreach (var entry in new[] { AiProfileNameEntry, AiBaseUrlEntry, AiModelEntry, AiKeyEntry, AiAccountEntry, AiIntervalEntry, AiHourlyEntry, AiSessionEntry })
            entry.TextChanged += (_, _) => MarkAiDirty();
        AiPromptEditor.TextChanged += (_, _) => MarkAiDirty();
        AiContactsSelector.SelectionChanged += (_, _) => MarkAiDirty();
        AiProtocolPicker.SelectedIndexChanged += (_, _) => MarkAiDirty();
        AiProtocolPicker.SelectedIndex = 0;
    }
    private void MarkAiDirty()
    {
        if (_aiLoading || !_aiLoaded) return;
        _aiDirty = true;
        AiConfigStatusLabel.Text = "有未保存修改。保存并切换后，预览与自动答复才会使用新配置。";
        UpdateAiAvailability();
    }
    private void UpdateAiAvailability()
    {
        var ready = _connected && !_busy && _sdkRunning && !_sdkBusy && !_aiBusy;
        var editable = ready && _aiLoaded && !_aiRunning;
        AiProviderColumn.IsEnabled = ready && !_aiRunning;
        AiNewProfileBtn.IsEnabled = editable;
        AiImportProfileBtn.IsEnabled = ready && !_aiRunning;
        AiProfilePicker.IsEnabled = editable;
        AiReplyColumn.IsEnabled = _connected && !_busy && _sdkRunning && !_sdkBusy;
        foreach (var view in new VisualElement[] { AiProfileNameEntry, AiProtocolPicker, AiBaseUrlEntry, AiModelEntry, AiKeyEntry, AiPromptEditor, AiContactsSelector, AiAccountEntry, AiIntervalEntry, AiHourlyEntry, AiSessionEntry, AiImportPromptBtn })
            view.IsEnabled = editable;
        AiGroupsCheck.IsEnabled = false;
        AiSaveBtn.IsEnabled = editable;
        AiReloadBtn.IsEnabled = ready && !_aiRunning;
        AiContactsBtn.IsEnabled = editable && !_contactsBusy;
        AiSampleEditor.IsEnabled = ready && _aiLoaded;
        AiPreviewBtn.IsEnabled = ready && _aiLoaded && !_aiDirty;
        AiStartBtn.IsEnabled = ready && _aiLoaded && !_aiRunning && !_aiDirty;
        AiStopBtn.IsEnabled = ready && _aiRunning;
    }
    private async Task LoadAiAsync(CancellationToken token)
    {
        if (!_sdkRunning) return;
        try
        {
            var response = await _api.SdkReadAsync("ai/config", token);
            token.ThrowIfCancellationRequested();
            var config = response.GetProperty("config");
            _aiLoading = true;
            _aiProfiles.Clear();
            if (config.TryGetProperty("profiles", out var profiles) && profiles.ValueKind == JsonValueKind.Array)
                foreach (var profile in profiles.EnumerateArray())
                    _aiProfiles.Add(new AiProfile(Text(profile, "id"), Text(profile, "name"), Text(profile, "protocol", "openai-compatible"), Text(profile, "baseUrl"), Text(profile, "model"), Text(profile, "apiKeyEnv"), Text(profile, "hasApiKey") == "True" || Text(profile, "hasApiKey") == "true"));
            AiProfilePicker.Items.Clear();
            foreach (var profile in _aiProfiles) AiProfilePicker.Items.Add(profile.Name);
            var active = Text(config, "activeProfileId");
            var index = _aiProfiles.FindIndex(p => p.Id == active);
            AiProfilePicker.SelectedIndex = index >= 0 ? index : _aiProfiles.Count > 0 ? 0 : -1;
            if (AiProfilePicker.SelectedIndex >= 0) ShowAiProfile(_aiProfiles[AiProfilePicker.SelectedIndex]);
            else ShowAiProfile(new AiProfile("default", "我的兼容接口", "openai-compatible", "", "", "", false));
            AiPromptEditor.Text = Text(config, "systemPrompt");
            AiContactsSelector.SetSelection(config.TryGetProperty("allowedContacts", out var contacts) && contacts.ValueKind == JsonValueKind.Array
                ? contacts.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!) : []);
            AiAccountEntry.Text = Text(config, "account");
            AiIntervalEntry.Text = Text(config, "minReplySeconds", "15");
            AiHourlyEntry.Text = Text(config, "maxRepliesPerHour", "20");
            AiSessionEntry.Text = Text(config, "sessionMinutes", "60");
            AiGroupsCheck.IsChecked = false;
            _aiLoaded = true;
            _aiDirty = false;
            AiConfigStatusLabel.Text = "已读取电脑端配置；密钥不会回显。";
            await RefreshAiStatusAsync(token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _aiLoaded = false;
            AiBadge.Text = "读取失败";
            AiConfigStatusLabel.Text = ex.Message;
            AiStatusLabel.Text = "AI 配置读取失败，请确认 SDK 服务已更新并启动。";
        }
        finally { _aiLoading = false; UpdateAiAvailability(); }
    }
    private void ShowAiProfile(AiProfile profile)
    {
        _aiEditingProfileId = profile.Id;
        AiProfileNameEntry.Text = profile.Name;
        AiProtocolPicker.SelectedIndex = profile.Protocol == "ollama" ? 1 : 0;
        AiBaseUrlEntry.Text = profile.BaseUrl;
        AiModelEntry.Text = profile.Model;
        AiKeyEntry.Text = "";
        AiKeyStatusLabel.Text = profile.HasKey ? "电脑端已保存密钥；留空保留，填入新密钥则替换。" :
            !string.IsNullOrWhiteSpace(profile.KeyEnvironment) ? "电脑端使用环境变量提供密钥；不会读取或显示其值。" : "尚未设置密钥。可稍后填写；本地 Ollama 可不需要密钥。";
    }
    private void OnAiProfileSelected(object? sender, EventArgs e)
    {
        if (_aiLoading || AiProfilePicker.SelectedIndex < 0) return;
        _aiLoading = true;
        ShowAiProfile(_aiProfiles[AiProfilePicker.SelectedIndex]);
        _aiLoading = false;
        MarkAiDirty();
    }
    private void OnAiNewProfileClicked(object? sender, EventArgs e)
    {
        if (!_aiLoaded || _aiRunning || _aiBusy) return;
        _aiLoading = true;
        AiProfilePicker.SelectedIndex = -1;
        ShowAiProfile(new AiProfile("profile-" + Guid.NewGuid().ToString("N")[..12], "新服务配置", "openai-compatible", "", "", "", false));
        _aiLoading = false;
        MarkAiDirty();
    }
    private async Task RefreshAiStatusAsync(CancellationToken token)
    {
        try
        {
            var result = await _api.SdkReadAsync("ai/status", token);
            token.ThrowIfCancellationRequested();
            _aiRunning = result.TryGetProperty("running", out var running) && running.ValueKind == JsonValueKind.True;
            var state = Text(result, "state");
            var error = Text(result, "lastError");
            AiBadge.Text = _aiRunning ? state is "starting" or "baselining" ? "正在准备" : "托管中" : error.Length > 0 ? "已停止 · 异常" : "已停止";
            AiBadge.TextColor = Color.FromArgb(error.Length > 0 ? "#914138" : _aiRunning ? "#11856B" : "#5E756F");
            AiStatusLabel.Text = error.Length > 0 ? "自动答复已暂停：" + error : _aiRunning
                ? $"自动答复正在运行 · 本次已答复 {Text(result, "replyCount", "0")} 条 · 仅处理启用后的新消息。"
                : "自动答复未启用。先保存配置并生成预览，再手动启用。";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { AiBadge.Text = "状态未知"; AiStatusLabel.Text = ex.Message; }
        finally { UpdateAiAvailability(); }
    }
    private static int AiNumber(Entry entry, string name, int min, int max)
    {
        if (!int.TryParse(entry.Text?.Trim(), out var value) || value < min || value > max)
            throw new ArgumentException($"{name}必须为 {min}–{max} 的整数。");
        return value;
    }
    private async void OnAiSaveClicked(object? sender, EventArgs e)
    {
        if (!_aiLoaded || _aiBusy || _aiRunning) return;
        try
        {
            var name = AiProfileNameEntry.Text?.Trim() ?? "";
            if (name.Length == 0) throw new ArgumentException("请填写服务配置名称。");
            if (AiContactsSelector.SelectedNames.Length > 10) throw new ArgumentException("当前版本最多选择 10 位自动答复用户，请减少勾选。");
            var old = _aiProfiles.FirstOrDefault(p => p.Id == _aiEditingProfileId);
            var profile = new Dictionary<string, object>
            {
                ["id"] = _aiEditingProfileId, ["name"] = name,
                ["protocol"] = AiProtocolPicker.SelectedIndex == 1 ? "ollama" : "openai-compatible",
                ["baseUrl"] = AiBaseUrlEntry.Text?.Trim() ?? "", ["model"] = AiModelEntry.Text?.Trim() ?? "",
                ["apiKeyEnv"] = old?.KeyEnvironment ?? ""
            };
            if (!string.IsNullOrWhiteSpace(AiKeyEntry.Text)) profile["apiKey"] = AiKeyEntry.Text.Trim();
            var profiles = _aiProfiles.Where(p => p.Id != _aiEditingProfileId).Select(p => (object)new
            { id = p.Id, name = p.Name, protocol = p.Protocol, baseUrl = p.BaseUrl, model = p.Model, apiKeyEnv = p.KeyEnvironment }).ToList();
            profiles.Add(profile);
            var body = new
            {
                activeProfileId = _aiEditingProfileId, profiles,
                systemPrompt = AiPromptEditor.Text ?? "", allowedContacts = AiContactsSelector.SelectedNames,
                account = AiAccountEntry.Text?.Trim() ?? "", allowGroups = false,
                minReplySeconds = AiNumber(AiIntervalEntry, "答复间隔", 10, 3600),
                maxRepliesPerHour = AiNumber(AiHourlyEntry, "每小时上限", 1, 120),
                sessionMinutes = AiNumber(AiSessionEntry, "托管时长", 1, 480)
            };
            _aiBusy = true; UpdateAiAvailability();
            AiConfigStatusLabel.Text = "正在保存到电脑端…";
            await _api.SdkActionAsync("ai/config", body, Token);
            AiKeyEntry.Text = "";
            await LoadAiAsync(Token);
            if (_aiLoaded) { AiConfigStatusLabel.Text = "已保存并切换服务配置。"; Notice("AI 配置已保存，自动答复仍需手动启用。", false); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AiConfigStatusLabel.Text = "保存失败：" + ex.Message; }
        finally { _aiBusy = false; UpdateAiAvailability(); }
    }
    private async void OnAiReloadClicked(object? sender, EventArgs e)
    {
        if (_aiBusy || _aiRunning || !_sdkRunning) return;
        _aiBusy = true; UpdateAiAvailability();
        try { await LoadAiAsync(Token); }
        catch (OperationCanceledException) { }
        finally { _aiBusy = false; UpdateAiAvailability(); }
    }
    private static async Task<string?> PickTextAsync(string title, string[] extensions, int maxBytes, CancellationToken token)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = title,
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.WinUI] = extensions,
                [DevicePlatform.Android] = extensions.Contains(".json") ? new[] { "application/json", "text/plain" } : new[] { "text/plain", "text/markdown" }
            })
        });
        if (file is null) return null;
        if (!extensions.Contains(Path.GetExtension(file.FileName), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("请选择受支持的文件：" + string.Join("、", extensions));
        using var source = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            if (memory.Length + read > maxBytes) throw new ArgumentException($"文件不得超过 {maxBytes / 1024} KB。");
            await memory.WriteAsync(buffer.AsMemory(0, read), token);
        }
        memory.Position = 0;
        using var reader = new StreamReader(memory, new System.Text.UTF8Encoding(false, true), true);
        return await reader.ReadToEndAsync(token);
    }
    private async void OnAiImportClicked(object? sender, EventArgs e)
    {
        if (_aiBusy || _aiRunning || !_sdkRunning) return;
        _aiBusy = true; UpdateAiAvailability();
        try
        {
            var json = await PickTextAsync("选择服务配置 JSON（兼容 CC Switch OpenAI 配置）", [".json"], 48 * 1024, Token);
            if (json is null) return;
            using var parsed = JsonDocument.Parse(json);
            await _api.SdkActionAsync("ai/profiles/import", new { json }, Token);
            await LoadAiAsync(Token);
            if (_aiLoaded) AiConfigStatusLabel.Text = "配置已导入到电脑端；请核对服务地址与模型。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AiConfigStatusLabel.Text = "导入失败：" + ex.Message; }
        finally { _aiBusy = false; UpdateAiAvailability(); }
    }
    private async void OnAiPromptImportClicked(object? sender, EventArgs e)
    {
        if (_aiBusy || _aiRunning || !_aiLoaded) return;
        try
        {
            var text = await PickTextAsync("导入角色提示词", [".txt", ".md"], 24 * 1024, Token);
            if (text is null) return;
            AiPromptEditor.Text = text;
            MarkAiDirty();
            AiConfigStatusLabel.Text = "提示词已载入，保存后生效。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AiConfigStatusLabel.Text = "提示词导入失败：" + ex.Message; }
    }
    private async void OnAiPreviewClicked(object? sender, EventArgs e)
    {
        if (_aiBusy || !_aiLoaded || _aiDirty) return;
        if (string.IsNullOrWhiteSpace(AiSampleEditor.Text)) { AiPreviewLabel.Text = "请先输入一条示例消息。"; return; }
        _aiBusy = true; UpdateAiAvailability();
        AiPreviewLabel.Text = "正在请求已配置的 AI 服务…";
        try
        {
            var result = await _api.SdkActionAsync("ai/preview", new { message = AiSampleEditor.Text }, Token);
            AiPreviewLabel.Text = Text(result, "reply", "服务未返回答复文本。");
        }
        catch (OperationCanceledException) { AiPreviewLabel.Text = "预览已取消。"; }
        catch (Exception ex) { AiPreviewLabel.Text = "预览失败：" + ex.Message; }
        finally { _aiBusy = false; UpdateAiAvailability(); }
    }
    private async void OnAiRunClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string action } || _aiBusy || !_aiLoaded || (action == "start" && _aiDirty)) return;
        _aiBusy = true; UpdateAiAvailability();
        AiStatusLabel.Text = action == "start" ? "正在建立新消息基线并启动自动答复…" : "正在停止自动答复…";
        try
        {
            await _api.SdkActionAsync("ai/" + action, new { }, Token);
            await RefreshAiStatusAsync(Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AiStatusLabel.Text = "操作失败：" + ex.Message; }
        finally { _aiBusy = false; UpdateAiAvailability(); }
    }
    private async void OnContactsClicked(object? sender, EventArgs e)
    {
        if (!_connected || !_sdkRunning || _contactsBusy) return;
        _contactsBusy = true; UpdateAvailability();
        ContactsStatusLabel.Text = "正在从电脑微信读取联系人，可能需要约一分钟…";
        if (sender == AiContactsBtn) AiStatusLabel.Text = ContactsStatusLabel.Text;
        try
        {
            var result = await _api.SdkReadAsync("contacts", Token);
            var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            if (result.TryGetProperty("data", out var accounts) && accounts.ValueKind == JsonValueKind.Array)
                foreach (var account in accounts.EnumerateArray())
                    if (account.TryGetProperty("names", out var values) && values.ValueKind == JsonValueKind.Array)
                        foreach (var name in values.EnumerateArray()) if (name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString())) names.Add(name.GetString()!);
            AiContactsSelector.AddCandidates(names);
            MomentsContactsSelector.AddCandidates(names);
            ContactsStatusLabel.Text = names.Count > 0 ? $"已载入 {names.Count} 位联系人。勾选后请保存对应配置。" : "微信未返回联系人；请确认微信已登录，也可手动添加姓名。";
            if (sender == AiContactsBtn) AiStatusLabel.Text = ContactsStatusLabel.Text;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ContactsStatusLabel.Text = "读取通讯录失败：" + ex.Message;
            if (sender == AiContactsBtn) AiStatusLabel.Text = ContactsStatusLabel.Text;
        }
        finally { _contactsBusy = false; UpdateAvailability(); }
    }
}
