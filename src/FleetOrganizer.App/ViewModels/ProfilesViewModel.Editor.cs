using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetOrganizer.App.Services;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Operations;
using FleetOrganizer.Core.Planning;
using FleetOrganizer.Core.Profiles;

namespace FleetOrganizer.App.ViewModels;

public partial class ProfilesViewModel
{
    [RelayCommand]
    private async Task PrepareFleetAsync()
    {
        if (!CanPrepareFleet)
        {
            StatusMessage = SelectedProfile is null
                ? "Choose or create a saved profile first."
                : HasUnsavedChanges
                    ? "Save the local profile changes before checking the live fleet."
                    : "Finish or cancel the active fleet run before preparing another one.";
            return;
        }

        await CompareCurrentProfileAsync();
    }

    [RelayCommand]
    private void ClearProfileSearch() => ProfileSearchText = string.Empty;

    [RelayCommand]
    private void ClearAssignmentSearch() => AssignmentSearchText = string.Empty;

    [RelayCommand]
    private void ShowAdvancedEditor() => IsAdvancedMode = true;

    [RelayCommand]
    private void SelectProfile(ProfileListItemViewModel? item)
    {
        if (item is not null)
        {
            SelectedProfile = item;
        }
    }

    [RelayCommand]
    private async Task TogglePinAsync(ProfileListItemViewModel? item)
    {
        item ??= SelectedProfile;
        if (item is null)
        {
            return;
        }

        item.IsPinned = !item.IsPinned;
        await SavePreferencesAsync();
        OnPropertyChanged(nameof(PinnedProfiles));
        StatusMessage = item.IsPinned
            ? $"Pinned '{item.Name}' to the FC console."
            : $"Unpinned '{item.Name}'.";
    }

    [RelayCommand]
    private async Task SetDefaultProfileAsync(ProfileListItemViewModel? item)
    {
        item ??= SelectedProfile;
        if (item is null)
        {
            return;
        }

        foreach (var profile in ProfileItems)
        {
            profile.IsDefault = profile.Id == item.Id;
        }

        item.IsPinned = true;
        SelectedProfile = item;
        await SavePreferencesAsync();
        OnPropertyChanged(nameof(PinnedProfiles));
        OnPropertyChanged(nameof(DefaultProfileName));
        StatusMessage = $"'{item.Name}' is now the default FC template.";
    }

    [RelayCommand]
    private async Task NewProfileAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var wingId = Guid.NewGuid();
            var squadId = Guid.NewGuid();
            var profile = new FleetProfile(
                Guid.NewGuid(),
                MakeUniqueProfileName("New Profile"),
                [new ProfileWing(wingId, "Wing 1", 0, [new ProfileSquad(squadId, "Squad 1", 0)])],
                []);
            await repository.SaveAsync(profile);
            await ReloadProfilesAsync(profile.Id);
            StatusMessage = "New profile created. Rename it and add your roster, then save.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Profile could not be created: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CaptureCurrentFleetAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Reading and capturing the current fleet…";
        try
        {
            var liveResult = await liveFleetService.LoadCurrentAsync();
            if (liveResult.Status != LiveFleetLoadStatus.Ready ||
                liveResult.Snapshot is null)
            {
                StatusMessage = liveResult.UserMessage;
                return;
            }

            var profile = FleetProfileFactory.FromLiveFleet(
                liveResult.Snapshot,
                MakeUniqueProfileName("Current Fleet"));
            await repository.SaveAsync(profile);
            await ReloadProfilesAsync(profile.Id);
            StatusMessage =
                $"Captured {profile.Assignments.Count} characters and {profile.Wings.Count} wings from fleet {liveResult.Snapshot.FleetId}.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Current fleet could not be captured: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        var profile = BuildCurrentProfile();
        var errors = ProfileValidator.Validate(profile);
        if (errors.Count > 0)
        {
            ValidationSummary = FormatValidation(errors);
            StatusMessage = "Fix the validation errors before saving.";
            return;
        }

        if (ProfileItems.Any(item =>
            item.Id != profile.Id &&
            string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationSummary = "Profile name is already in use.";
            StatusMessage = "Choose a unique profile name before saving.";
            return;
        }

        IsBusy = true;
        try
        {
            await repository.SaveAsync(profile);
            await ReloadProfilesAsync(profile.Id);
            StatusMessage = $"Saved '{profile.Name}'.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Profile could not be saved: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DuplicateProfileAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        var source = BuildCurrentProfile();
        var duplicate = FleetProfileFactory.Duplicate(
            source,
            MakeUniqueProfileName($"{source.Name} Copy"));
        IsBusy = true;
        try
        {
            await repository.SaveAsync(duplicate);
            await ReloadProfilesAsync(duplicate.Id);
            StatusMessage = $"Created '{duplicate.Name}'.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Profile could not be duplicated: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        if (!userInteraction.Confirm(
            "Delete profile",
            $"Delete profile '{ProfileName}'?\n\nThis removes only the saved local profile. It does not change the EVE fleet.",
            UserConfirmationKind.Warning))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await repository.DeleteAsync(EditingProfileId);
            await ReloadProfilesAsync(null);
            await SavePreferencesAsync();
            StatusMessage = "Profile deleted. No EVE fleet changes were made.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Profile could not be deleted: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportProfileAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        var profile = BuildCurrentProfile();
        var errors = ProfileValidator.Validate(profile);
        if (errors.Count > 0)
        {
            ValidationSummary = FormatValidation(errors);
            StatusMessage = "Fix validation errors before exporting.";
            return;
        }

        var path = fileDialogs.ChooseSavePath(
            "Export Fleet Desk setup",
            $"{GetSafeFileName(profile.Name)}.fleet-profile.json",
            "Fleet Desk setup (*.json)|*.json|All files (*.*)|*.*",
            ".json");
        if (path is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(
                path,
                FleetProfileJsonSerializer.Serialize(profile),
                Encoding.UTF8);
            StatusMessage = $"Exported '{profile.Name}'.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Profile could not be exported: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportProfileAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var path = fileDialogs.ChooseOpenPath(
            "Import Fleet Desk setup",
            "Fleet Desk setup (*.json)|*.json|All files (*.*)|*.*");
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8);
            var imported = FleetProfileJsonSerializer.Deserialize(json);
            var errors = ProfileValidator.Validate(imported);
            if (errors.Count > 0)
            {
                StatusMessage = $"Imported profile is invalid: {errors[0].Message}";
                return;
            }

            var copy = FleetProfileFactory.Duplicate(
                imported,
                MakeUniqueProfileName(imported.Name));
            await repository.SaveAsync(copy);
            await ReloadProfilesAsync(copy.Id);
            StatusMessage = $"Imported '{copy.Name}'.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Profile could not be imported: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResolveRosterAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        var parsedEntries = RosterPasteParser.Parse(RosterPasteText);
        if (parsedEntries.Length == 0)
        {
            StatusMessage = "Paste at least one EVE character name first.";
            return;
        }

        if (SquadOptions.Count == 0)
        {
            StatusMessage = "Add at least one squad before adding characters.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Resolving {parsedEntries.Length} exact EVE character name{(parsedEntries.Length == 1 ? string.Empty : "s")}…";
        try
        {
            var result = await characterNameResolver.ResolveAsync(
                parsedEntries.Select(entry => entry.CharacterName).ToArray());
            var parsedByName = parsedEntries.ToDictionary(
                entry => entry.CharacterName,
                StringComparer.OrdinalIgnoreCase);
            var existingCharacterIds = Assignments
                .Select(assignment => assignment.CharacterId)
                .ToHashSet();
            var addedCount = 0;
            var duplicateCount = 0;

            foreach (var character in result.Resolved)
            {
                if (!existingCharacterIds.Add(character.CharacterId))
                {
                    duplicateCount++;
                    continue;
                }

                parsedByName.TryGetValue(character.CharacterName, out var parsedEntry);
                var targetSquad = FindSquadOption(parsedEntry?.SquadName) ?? SquadOptions[0];
                var assignment = new ProfileAssignmentEditorViewModel(
                    character.CharacterId,
                    character.CharacterName,
                    targetSquad.Id,
                    ParseRole(parsedEntry?.RoleText),
                    string.Empty);
                HookAssignment(assignment);
                Assignments.Add(assignment);
                addedCount++;
            }

            UnresolvedRosterEntries.Clear();
            foreach (var unresolvedName in result.UnresolvedNames)
            {
                UnresolvedRosterEntries.Add($"{unresolvedName} — no exact character match");
            }

            RosterPasteText = result.UnresolvedNames.Length == 0
                ? string.Empty
                : string.Join(Environment.NewLine, result.UnresolvedNames);
            StatusMessage = result.UserMessage ??
                $"Added {addedCount}; skipped {duplicateCount} already assigned; {result.UnresolvedNames.Length} unresolved.";
            if (addedCount > 0)
            {
                MarkEditorDirty();
                FilteredAssignments.Refresh();
                RefreshAssignmentSummary();
            }

            RefreshValidation();
            InvalidateDryRun();
        }
        catch (Exception exception)
        {
            StatusMessage = $"Roster could not be resolved: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CompareCurrentProfileAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        var profile = BuildCurrentProfile();
        var errors = ProfileValidator.Validate(profile);
        if (errors.Count > 0)
        {
            ValidationSummary = FormatValidation(errors);
            StatusMessage = "Fix the validation errors before comparing this profile.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Reading the current fleet and building a dry run…";
        try
        {
            var liveResult = await liveFleetService.LoadCurrentAsync();
            if (liveResult.Status != LiveFleetLoadStatus.Ready || liveResult.Snapshot is null)
            {
                InvalidateDryRun();
                StatusMessage = liveResult.UserMessage;
                return;
            }

            UpdateObservedShipTypes(liveResult.Snapshot);
            var resolution = ShipRuleResolver.Resolve(profile, liveResult.Snapshot);
            lastPreparedProfile = resolution.EffectiveProfile;
            lastShipRuleMatchCount = resolution.Matches.Count;
            lastShipRuleCapacitySkipCount = resolution.CapacitySkipped.Count;
            var plan = FleetPlanModeFilter.Apply(
                FleetPlanner.Build(resolution.EffectiveProfile, liveResult.Snapshot),
                SelectedRunMode);
            ApplyDryRun(plan, liveResult.Snapshot.ConfirmedAtUtc);
            await SavePreferencesAsync();
            StatusMessage = plan.BlockingIssues == 0
                ? $"Preview ready: {plan.TotalChanges} proposed change{(plan.TotalChanges == 1 ? string.Empty : "s")}" +
                    (lastShipRuleMatchCount == 0
                        ? "."
                        : $"; {lastShipRuleMatchCount} live character{(lastShipRuleMatchCount == 1 ? string.Empty : "s")} matched by ship type.")
                : $"Dry run found {plan.BlockingIssues} blocking issue{(plan.BlockingIssues == 1 ? string.Empty : "s")}.";
        }
        catch (Exception exception)
        {
            InvalidateDryRun();
            StatusMessage = $"Dry run could not be built: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AddWing()
    {
        if (!CanEdit)
        {
            return;
        }

        var wing = new ProfileWingEditorViewModel(
            Guid.NewGuid(),
            MakeUniqueHierarchyName(
                $"Wing {Wings.Count + 1}",
                Wings.Select(existing => existing.Name).ToArray()));
        var squad = new ProfileSquadEditorViewModel(Guid.NewGuid(), "Squad 1");
        wing.Squads.Add(squad);
        HookWing(wing);
        Wings.Add(wing);
        UpdateSquadOptions();
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    [RelayCommand]
    private void AddSquad(ProfileWingEditorViewModel? wing)
    {
        if (!CanEdit || wing is null)
        {
            return;
        }

        var squad = new ProfileSquadEditorViewModel(
            Guid.NewGuid(),
            MakeUniqueHierarchyName(
                $"Squad {wing.Squads.Count + 1}",
                wing.Squads.Select(existing => existing.Name).ToArray()));
        HookSquad(squad);
        wing.Squads.Add(squad);
        UpdateSquadOptions();
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    [RelayCommand]
    private void DuplicateWing(ProfileWingEditorViewModel? wing)
    {
        if (!CanEdit || wing is null)
        {
            return;
        }

        var copy = new ProfileWingEditorViewModel(
            Guid.NewGuid(),
            MakeUniqueHierarchyName(
                $"{wing.Name} Copy",
                Wings.Select(existing => existing.Name).ToArray()));
        foreach (var squad in wing.Squads)
        {
            copy.Squads.Add(new ProfileSquadEditorViewModel(Guid.NewGuid(), squad.Name));
        }

        HookWing(copy);
        Wings.Insert(Wings.IndexOf(wing) + 1, copy);
        UpdateSquadOptions();
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    [RelayCommand]
    private void DuplicateSquad(ProfileSquadEditorViewModel? squad)
    {
        var wing = FindParentWing(squad);
        if (!CanEdit || squad is null || wing is null)
        {
            return;
        }

        var copy = new ProfileSquadEditorViewModel(
            Guid.NewGuid(),
            MakeUniqueHierarchyName(
                $"{squad.Name} Copy",
                wing.Squads.Select(existing => existing.Name).ToArray()));
        HookSquad(copy);
        wing.Squads.Insert(wing.Squads.IndexOf(squad) + 1, copy);
        UpdateSquadOptions();
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    [RelayCommand]
    private void MoveWingUp(ProfileWingEditorViewModel? wing) => MoveWing(wing, -1);

    [RelayCommand]
    private void MoveWingDown(ProfileWingEditorViewModel? wing) => MoveWing(wing, 1);

    [RelayCommand]
    private void MoveSquadUp(ProfileSquadEditorViewModel? squad) => MoveSquad(squad, -1);

    [RelayCommand]
    private void MoveSquadDown(ProfileSquadEditorViewModel? squad) => MoveSquad(squad, 1);

    [RelayCommand]
    private void DeleteWing(ProfileWingEditorViewModel? wing)
    {
        if (!CanEdit || wing is null)
        {
            return;
        }

        var squadIds = wing.Squads.Select(squad => squad.Id).ToHashSet();
        if (Assignments.Any(assignment => squadIds.Contains(assignment.TargetSquadId)) ||
            ShipRules.Any(rule => squadIds.Contains(rule.TargetSquadId) ||
                (rule.OverflowSquadId is Guid overflowId && squadIds.Contains(overflowId))))
        {
            StatusMessage = "Move or remove characters and ship rules assigned to this wing before deleting it.";
            return;
        }

        UnhookWing(wing);
        Wings.Remove(wing);
        UpdateSquadOptions();
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    [RelayCommand]
    private void DeleteSquad(ProfileSquadEditorViewModel? squad)
    {
        var wing = FindParentWing(squad);
        if (!CanEdit || squad is null || wing is null)
        {
            return;
        }

        if (Assignments.Any(assignment => assignment.TargetSquadId == squad.Id) ||
            ShipRules.Any(rule => rule.TargetSquadId == squad.Id || rule.OverflowSquadId == squad.Id))
        {
            StatusMessage = "Move or remove characters and ship rules assigned to this squad before deleting it.";
            return;
        }

        UnhookSquad(squad);
        wing.Squads.Remove(squad);
        UpdateSquadOptions();
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    [RelayCommand]
    private void AddShipRule()
    {
        if (!CanEdit || SquadOptions.Count == 0)
        {
            StatusMessage = "Add a squad before creating a ship placement rule.";
            return;
        }

        var rule = new ProfileShipRuleEditorViewModel(
            Guid.NewGuid(),
            string.Empty,
            SquadOptions[0].Id,
            label: $"Ship group {ShipRules.Count + 1}");
        HookShipRule(rule);
        ShipRules.Add(rule);
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
        StatusMessage = "Add one or more exact ship types, then choose primary and optional overflow squads.";
    }

    [RelayCommand]
    private void MoveShipRuleUp(ProfileShipRuleEditorViewModel? rule) => MoveShipRule(rule, -1);

    [RelayCommand]
    private void MoveShipRuleDown(ProfileShipRuleEditorViewModel? rule) => MoveShipRule(rule, 1);

    [RelayCommand]
    private void ClearShipRuleOverflow(ProfileShipRuleEditorViewModel? rule)
    {
        if (CanEdit && rule is not null)
        {
            rule.OverflowSquadId = null;
        }
    }

    [RelayCommand]
    private void DeleteShipRule(ProfileShipRuleEditorViewModel? rule)
    {
        if (!CanEdit || rule is null)
        {
            return;
        }

        UnhookShipRule(rule);
        ShipRules.Remove(rule);
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
        StatusMessage = "Ship placement rule removed from this local profile.";
    }

    public void MoveAssignmentsToSquad(Guid squadId, long draggedCharacterId)
    {
        if (!CanEdit || !SquadOptions.Any(option => option.Id == squadId))
        {
            return;
        }

        var dragged = Assignments.FirstOrDefault(
            assignment => assignment.CharacterId == draggedCharacterId);
        if (dragged is null)
        {
            return;
        }

        if (!dragged.IsSelected)
        {
            foreach (var assignment in Assignments)
            {
                assignment.IsSelected = false;
            }

            dragged.IsSelected = true;
        }

        var selected = Assignments
            .Where(assignment => assignment.IsSelected)
            .ToArray();
        foreach (var assignment in selected)
        {
            assignment.TargetSquadId = squadId;
        }

        var squadName = SquadOptions.First(option => option.Id == squadId).DisplayName;
        StatusMessage = $"Moved {selected.Length} character{(selected.Length == 1 ? string.Empty : "s")} to {squadName}. Save when the layout looks right.";
        RefreshSquadCards();
    }

    [RelayCommand]
    private void SelectAllAssignments()
    {
        foreach (var assignment in FilteredAssignments.Cast<ProfileAssignmentEditorViewModel>())
        {
            assignment.IsSelected = true;
        }

        RefreshAssignmentSummary();
    }

    [RelayCommand]
    private void ClearAssignmentSelection()
    {
        foreach (var assignment in Assignments)
        {
            assignment.IsSelected = false;
        }

        RefreshAssignmentSummary();
    }

    [RelayCommand]
    private void ApplyBulkSquad()
    {
        if (BulkTargetSquadId is not Guid squadId)
        {
            StatusMessage = "Choose a target squad first.";
            return;
        }

        ApplyToSelected(assignment => assignment.TargetSquadId = squadId, "squad");
    }

    [RelayCommand]
    private void ApplyBulkRole() =>
        ApplyToSelected(assignment => assignment.DesiredRole = BulkDesiredRole, "role");

    [RelayCommand]
    private void ApplyBulkTags()
    {
        var normalizedTags = string.Join(", ", ParseTags(BulkTagsText));
        ApplyToSelected(assignment => assignment.TagsText = normalizedTags, "tags");
    }

    [RelayCommand]
    private void RemoveSelectedAssignments()
    {
        var selected = Assignments.Where(assignment => assignment.IsSelected).ToArray();
        foreach (var assignment in selected)
        {
            UnhookAssignment(assignment);
            Assignments.Remove(assignment);
        }

        StatusMessage = selected.Length == 0
            ? "Select at least one character first."
            : $"Removed {selected.Length} character{(selected.Length == 1 ? string.Empty : "s")} from this local profile.";
        RefreshValidation();
        if (selected.Length > 0)
        {
            MarkEditorDirty();
            FilteredAssignments.Refresh();
            RefreshAssignmentSummary();
            InvalidateDryRun();
        }
    }

    partial void OnSelectedProfileChanged(ProfileListItemViewModel? value)
    {
        if (value is null)
        {
            ClearEditor();
            return;
        }

        LoadEditor(value.Profile);
        if (!isLoadingPreferences)
        {
            _ = SavePreferencesSafelyAsync();
        }
    }

    partial void OnProfileSearchTextChanged(string value)
    {
        _ = value;
        FilteredProfileItems.Refresh();
        OnPropertyChanged(nameof(ProfileSearchSummary));
    }

    partial void OnAssignmentSearchTextChanged(string value)
    {
        _ = value;
        FilteredAssignments.Refresh();
        OnPropertyChanged(nameof(AssignmentSearchSummary));
    }

    partial void OnProfileNameChanged(string value)
    {
        _ = value;
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    partial void OnShowAlreadyCorrectChanged(bool value)
    {
        _ = value;
        RefreshVisibleDryRunItems();
    }

    partial void OnSelectedRunModeChanged(FleetRunMode value)
    {
        _ = value;
        InvalidateDryRun();
        if (!isLoadingPreferences)
        {
            _ = SavePreferencesSafelyAsync();
        }
    }

    partial void OnAttentionSoundsEnabledChanged(bool value)
    {
        _ = value;
        if (!isLoadingPreferences)
        {
            _ = SavePreferencesSafelyAsync();
        }
    }

    partial void OnFleetPollingSecondsChanged(int value) => SaveOperationalPreference(value is >= 15 and <= 300);

    partial void OnInvitationCheckSecondsChanged(int value) => SaveOperationalPreference(value is >= 15 and <= 300);

    partial void OnInvitationTimeoutMinutesChanged(int value) => SaveOperationalPreference(value is >= 1 and <= 120);

    partial void OnStartMinimizedChanged(bool value)
    {
        _ = value;
        SaveOperationalPreference(isValid: true);
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        _ = value;
        SaveOperationalPreference(isValid: true);
    }

    partial void OnSelectedThemeChanged(FleetDeskTheme value) => SaveOperationalPreference(Enum.IsDefined(value));

    private void SaveOperationalPreference(bool isValid)
    {
        if (!isLoadingPreferences && isValid)
        {
            _ = SavePreferencesSafelyAsync();
        }
    }

    private async Task ReloadProfilesAsync(Guid? selectedProfileId)
    {
        var profiles = await repository.LoadAllAsync();
        ProfileItems.Clear();
        foreach (var profile in profiles
            .OrderByDescending(profile => profile.Id == preferences.DefaultProfileId)
            .ThenByDescending(profile => preferences.PinnedProfileIds.Contains(profile.Id))
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase))
        {
            ProfileItems.Add(new ProfileListItemViewModel(profile)
            {
                IsDefault = profile.Id == preferences.DefaultProfileId,
                IsPinned = preferences.PinnedProfileIds.Contains(profile.Id) ||
                    profile.Id == preferences.DefaultProfileId,
            });
        }

        FilteredProfileItems.Refresh();
        OnPropertyChanged(nameof(ProfileSearchSummary));
        OnPropertyChanged(nameof(PinnedProfiles));
        OnPropertyChanged(nameof(DefaultProfileName));

        SelectedProfile = selectedProfileId is Guid id
            ? ProfileItems.FirstOrDefault(item => item.Id == id) ?? ProfileItems.FirstOrDefault()
            : ProfileItems.FirstOrDefault();
        if (SelectedProfile is null)
        {
            ClearEditor();
        }
    }

    private async Task SavePreferencesAsync()
    {
        await preferencesGate.WaitAsync();
        try
        {
            preferences = new FleetDeskPreferences
            {
                LastUsedProfileId = SelectedProfile?.Id ?? preferences.LastUsedProfileId,
                DefaultProfileId = ProfileItems.FirstOrDefault(item => item.IsDefault)?.Id,
                PinnedProfileIds = ProfileItems
                    .Where(item => item.IsPinned)
                    .Select(item => item.Id)
                    .Distinct()
                    .ToArray(),
                RunMode = SelectedRunMode,
                AttentionSoundsEnabled = AttentionSoundsEnabled,
                FleetPollingSeconds = Math.Clamp(FleetPollingSeconds, 15, 300),
                InvitationCheckSeconds = Math.Clamp(InvitationCheckSeconds, 15, 300),
                InvitationTimeoutMinutes = Math.Clamp(InvitationTimeoutMinutes, 1, 120),
                StartMinimized = StartMinimized,
                MinimizeToTray = MinimizeToTray,
                Theme = SelectedTheme,
            };
            await preferencesRepository.SaveAsync(preferences);
        }
        finally
        {
            preferencesGate.Release();
        }
    }

    private async Task SavePreferencesSafelyAsync()
    {
        try
        {
            await SavePreferencesAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = $"Local Fleet Desk preferences could not be saved: {exception.Message}";
        }
    }

    private void LoadEditor(FleetProfile profile)
    {
        InvalidateDryRun();
        isLoadingEditor = true;
        ClearEditorCollections();
        EditingProfileId = profile.Id;
        ProfileName = profile.Name;

        foreach (var wing in profile.Wings.OrderBy(wing => wing.SortOrder))
        {
            var wingEditor = new ProfileWingEditorViewModel(wing.Id, wing.Name);
            foreach (var squad in wing.Squads.OrderBy(squad => squad.SortOrder))
            {
                wingEditor.Squads.Add(new ProfileSquadEditorViewModel(squad.Id, squad.Name));
            }

            HookWing(wingEditor);
            Wings.Add(wingEditor);
        }

        foreach (var assignment in profile.Assignments
            .OrderBy(assignment => assignment.CharacterName, StringComparer.OrdinalIgnoreCase))
        {
            var editor = new ProfileAssignmentEditorViewModel(
                assignment.CharacterId,
                assignment.CharacterName,
                assignment.TargetSquadId,
                assignment.DesiredRole,
                string.Join(", ", assignment.Tags));
            HookAssignment(editor);
            Assignments.Add(editor);
        }

        foreach (var rule in profile.ShipRules.OrderBy(rule => rule.SortOrder))
        {
            var editor = new ProfileShipRuleEditorViewModel(
                rule.Id,
                rule.ShipTypeName,
                rule.TargetSquadId,
                rule.Label,
                rule.OverflowSquadId,
                rule.MaximumPerSquad,
                rule.BalanceAcrossTargets,
                rule.IsFallback);
            HookShipRule(editor);
            ShipRules.Add(editor);
        }

        UnresolvedRosterEntries.Clear();
        RosterPasteText = string.Empty;
        IsEditorActive = true;
        UpdateSquadOptions();
        isLoadingEditor = false;
        HasUnsavedChanges = false;
        FilteredAssignments.Refresh();
        RefreshAssignmentSummary();
        RefreshSquadCards();
        RefreshValidation();
    }

    private void ClearEditor()
    {
        InvalidateDryRun();
        isLoadingEditor = true;
        ClearEditorCollections();
        EditingProfileId = Guid.Empty;
        ProfileName = string.Empty;
        IsEditorActive = false;
        SquadOptions.Clear();
        ObservedShipTypes.Clear();
        UnresolvedRosterEntries.Clear();
        ValidationSummary = "No profile selected.";
        isLoadingEditor = false;
        HasUnsavedChanges = false;
        FilteredAssignments.Refresh();
        RefreshAssignmentSummary();
    }

    private void ClearEditorCollections()
    {
        foreach (var wing in Wings)
        {
            UnhookWing(wing);
        }

        foreach (var assignment in Assignments)
        {
            UnhookAssignment(assignment);
        }

        foreach (var rule in ShipRules)
        {
            UnhookShipRule(rule);
        }

        Wings.Clear();
        Assignments.Clear();
        ShipRules.Clear();
    }

    private FleetProfile BuildCurrentProfile() =>
        new FleetProfile(
            EditingProfileId,
            ProfileName.Trim(),
            Wings.Select((wing, wingIndex) => new ProfileWing(
                wing.Id,
                wing.Name.Trim(),
                wingIndex,
                wing.Squads.Select((squad, squadIndex) => new ProfileSquad(
                    squad.Id,
                    squad.Name.Trim(),
                    squadIndex)).ToArray())).ToArray(),
            Assignments.Select(assignment => new ProfileAssignment(
                assignment.CharacterId,
                assignment.CharacterName,
                assignment.TargetSquadId,
                assignment.DesiredRole)
            {
                Tags = ParseTags(assignment.TagsText),
            }).ToArray())
        {
            ShipRules = ShipRules.Select((rule, ruleIndex) => new ProfileShipRule(
                rule.Id,
                rule.ShipTypeName.Trim(),
                rule.TargetSquadId,
                ruleIndex)
            {
                Label = rule.Label.Trim(),
                OverflowSquadId = rule.OverflowSquadId,
                MaximumPerSquad = rule.MaximumPerSquad,
                BalanceAcrossTargets = rule.BalanceAcrossTargets,
                IsFallback = rule.IsFallback,
            }).ToArray(),
        };

    private void RefreshValidation()
    {
        if (isLoadingEditor || !IsEditorActive)
        {
            return;
        }

        var errors = ProfileValidator.Validate(BuildCurrentProfile());
        ValidationSummary = errors.Count == 0
            ? $"Ready • {Assignments.Count} exact characters • {ShipRules.Count} ship rules • {SquadOptions.Count} squads"
            : FormatValidation(errors);
    }

    private void UpdateSquadOptions()
    {
        var previousBulkTarget = BulkTargetSquadId;
        SquadOptions.Clear();
        foreach (var wing in Wings)
        {
            foreach (var squad in wing.Squads)
            {
                SquadOptions.Add(new ProfileSquadOptionViewModel(
                    squad.Id,
                    $"{wing.Name} / {squad.Name}"));
            }
        }

        BulkTargetSquadId = previousBulkTarget is Guid previousId &&
            SquadOptions.Any(option => option.Id == previousId)
                ? previousId
                : SquadOptions.FirstOrDefault()?.Id;
        RefreshSquadCards();
    }

    private void HookWing(ProfileWingEditorViewModel wing)
    {
        wing.PropertyChanged += OnHierarchyPropertyChanged;
        foreach (var squad in wing.Squads)
        {
            HookSquad(squad);
        }
    }

    private void UnhookWing(ProfileWingEditorViewModel wing)
    {
        wing.PropertyChanged -= OnHierarchyPropertyChanged;
        foreach (var squad in wing.Squads)
        {
            UnhookSquad(squad);
        }
    }

    private void HookSquad(ProfileSquadEditorViewModel squad)
    {
        squad.PropertyChanged += OnHierarchyPropertyChanged;
    }

    private void UnhookSquad(ProfileSquadEditorViewModel squad)
    {
        squad.PropertyChanged -= OnHierarchyPropertyChanged;
    }

    private void HookAssignment(ProfileAssignmentEditorViewModel assignment)
    {
        assignment.PropertyChanged += OnAssignmentPropertyChanged;
    }

    private void UnhookAssignment(ProfileAssignmentEditorViewModel assignment)
    {
        assignment.PropertyChanged -= OnAssignmentPropertyChanged;
    }

    private void HookShipRule(ProfileShipRuleEditorViewModel rule)
    {
        rule.PropertyChanged += OnShipRulePropertyChanged;
    }

    private void UnhookShipRule(ProfileShipRuleEditorViewModel rule)
    {
        rule.PropertyChanged -= OnShipRulePropertyChanged;
    }

    private void OnHierarchyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (string.Equals(e.PropertyName, "Name", StringComparison.Ordinal))
        {
            UpdateSquadOptions();
        }

        MarkEditorDirty();
        FilteredAssignments.Refresh();
        RefreshValidation();
        InvalidateDryRun();
    }

    private void OnAssignmentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        RefreshAssignmentSummary();
        RefreshValidation();
        if (!string.Equals(e.PropertyName, nameof(ProfileAssignmentEditorViewModel.IsSelected), StringComparison.Ordinal))
        {
            MarkEditorDirty();
            FilteredAssignments.Refresh();
            if (string.Equals(e.PropertyName, nameof(ProfileAssignmentEditorViewModel.TargetSquadId), StringComparison.Ordinal))
            {
                RefreshSquadCards();
            }

            InvalidateDryRun();
        }
    }

    private void OnShipRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    private bool FilterProfile(object item)
    {
        if (item is not ProfileListItemViewModel profile || string.IsNullOrWhiteSpace(ProfileSearchText))
        {
            return true;
        }

        return profile.Name.Contains(ProfileSearchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterAssignment(object item)
    {
        if (item is not ProfileAssignmentEditorViewModel assignment ||
            string.IsNullOrWhiteSpace(AssignmentSearchText))
        {
            return true;
        }

        var search = AssignmentSearchText.Trim();
        var squadName = SquadOptions
            .FirstOrDefault(option => option.Id == assignment.TargetSquadId)
            ?.DisplayName;
        return assignment.CharacterName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            assignment.TagsText.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            (squadName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
            assignment.DesiredRole.ToString().Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void MarkEditorDirty()
    {
        if (!isLoadingEditor && IsEditorActive)
        {
            HasUnsavedChanges = true;
        }
    }

    private void RefreshAssignmentSummary()
    {
        OnPropertyChanged(nameof(AssignmentSearchSummary));
        RefreshSquadCards();
    }

    private void RefreshSquadCards()
    {
        foreach (var squad in Wings.SelectMany(wing => wing.Squads))
        {
            var assigned = Assignments
                .Where(assignment => assignment.TargetSquadId == squad.Id)
                .OrderBy(assignment => assignment.CharacterName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            squad.AssignmentCount = assigned.Length;
            squad.CharacterPreview = assigned.Length == 0
                ? "Drop characters here"
                : string.Join(", ", assigned.Take(4).Select(assignment => assignment.CharacterName)) +
                    (assigned.Length > 4 ? $" +{assigned.Length - 4}" : string.Empty);
        }
    }

    private void UpdateObservedShipTypes(LiveFleetSnapshot snapshot)
    {
        var selectedShipNames = ShipRules
            .SelectMany(rule => ShipRuleResolver.ParseShipTypes(rule.ShipTypeName))
            .Where(name => name.Length > 0);
        var names = snapshot.Members
            .Select(member => member.ShipTypeName.Trim())
            .Concat(selectedShipNames)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ObservedShipTypes.Clear();
        foreach (var name in names)
        {
            ObservedShipTypes.Add(name);
        }
    }

    private void MoveWing(ProfileWingEditorViewModel? wing, int offset)
    {
        if (!CanEdit || wing is null)
        {
            return;
        }

        MoveItem(Wings, wing, offset);
        UpdateSquadOptions();
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    private void MoveShipRule(ProfileShipRuleEditorViewModel? rule, int offset)
    {
        if (!CanEdit || rule is null)
        {
            return;
        }

        MoveItem(ShipRules, rule, offset);
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    private void MoveSquad(ProfileSquadEditorViewModel? squad, int offset)
    {
        var wing = FindParentWing(squad);
        if (!CanEdit || squad is null || wing is null)
        {
            return;
        }

        MoveItem(wing.Squads, squad, offset);
        UpdateSquadOptions();
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    private ProfileWingEditorViewModel? FindParentWing(ProfileSquadEditorViewModel? squad) =>
        squad is null
            ? null
            : Wings.FirstOrDefault(wing => wing.Squads.Contains(squad));

    private ProfileSquadOptionViewModel? FindSquadOption(string? requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            return null;
        }

        var normalized = requestedName.Trim();
        var fullMatch = SquadOptions.FirstOrDefault(option =>
            string.Equals(option.DisplayName, normalized, StringComparison.OrdinalIgnoreCase));
        if (fullMatch is not null)
        {
            return fullMatch;
        }

        var leafMatches = Wings
            .SelectMany(wing => wing.Squads)
            .Where(squad => string.Equals(squad.Name, normalized, StringComparison.OrdinalIgnoreCase))
            .Select(squad => SquadOptions.First(option => option.Id == squad.Id))
            .ToArray();
        return leafMatches.Length == 1 ? leafMatches[0] : null;
    }

    private void ApplyToSelected(
        Action<ProfileAssignmentEditorViewModel> action,
        string fieldName)
    {
        var selected = Assignments.Where(assignment => assignment.IsSelected).ToArray();
        foreach (var assignment in selected)
        {
            action(assignment);
        }

        StatusMessage = selected.Length == 0
            ? "Select at least one character first."
            : $"Updated {fieldName} for {selected.Length} selected character{(selected.Length == 1 ? string.Empty : "s")}.";
        RefreshValidation();
    }

    private string MakeUniqueProfileName(string baseName)
    {
        var existingNames = ProfileItems
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (existingNames.Add(baseName))
        {
            return baseName;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (existingNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string MakeUniqueHierarchyName(string baseName, string[] existingNames)
    {
        var names = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedBase = baseName.Trim();
        var firstCandidate = normalizedBase[..Math.Min(
            normalizedBase.Length,
            ProfileValidator.MaximumHierarchyNameLength)];
        if (names.Add(firstCandidate))
        {
            return firstCandidate;
        }

        for (var suffix = 2; ; suffix++)
        {
            var suffixText = $" {suffix}";
            var prefixLength = Math.Max(
                1,
                ProfileValidator.MaximumHierarchyNameLength - suffixText.Length);
            var prefix = normalizedBase[..Math.Min(normalizedBase.Length, prefixLength)];
            var candidate = $"{prefix}{suffixText}";
            if (names.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static void MoveItem<T>(ObservableCollection<T> items, T item, int offset)
    {
        var currentIndex = items.IndexOf(item);
        var targetIndex = currentIndex + offset;
        if (currentIndex >= 0 && targetIndex >= 0 && targetIndex < items.Count)
        {
            items.Move(currentIndex, targetIndex);
        }
    }

    private static DesiredFleetRole ParseRole(string? value)
    {
        var normalized = value?
            .Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized switch
        {
            "fleetcommander" or "fleetboss" or "fc" => DesiredFleetRole.FleetCommander,
            "wingcommander" or "wc" => DesiredFleetRole.WingCommander,
            "squadcommander" or "sc" => DesiredFleetRole.SquadCommander,
            _ => DesiredFleetRole.SquadMember,
        };
    }

    private static string[] ParseTags(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(
                    TagSeparators,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static string FormatValidation(IReadOnlyList<ProfileValidationError> errors) =>
        string.Join(" • ", errors.Take(3).Select(error => error.Message));

}
