using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Profiles;
using Microsoft.Win32;

namespace FleetOrganizer.App.ViewModels;

public partial class ProfilesViewModel : ObservableObject
{
    private static readonly char[] TagSeparators = [',', ';'];

    private readonly IFleetProfileRepository repository;
    private readonly ICharacterNameResolver characterNameResolver;
    private readonly ILiveFleetService liveFleetService;
    private bool isLoadingEditor;

    [ObservableProperty]
    public partial ProfileListItemViewModel? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial Guid EditingProfileId { get; set; }

    [ObservableProperty]
    public partial string ProfileName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsEditorActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        "Create a profile or capture the current live fleet.";

    [ObservableProperty]
    public partial string ValidationSummary { get; set; } = "No profile selected.";

    [ObservableProperty]
    public partial string RosterPasteText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Guid? BulkTargetSquadId { get; set; }

    [ObservableProperty]
    public partial DesiredFleetRole BulkDesiredRole { get; set; } =
        DesiredFleetRole.SquadMember;

    [ObservableProperty]
    public partial string BulkTagsText { get; set; } = string.Empty;

    public ProfilesViewModel(
        IFleetProfileRepository repository,
        ICharacterNameResolver characterNameResolver,
        ILiveFleetService liveFleetService)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(characterNameResolver);
        ArgumentNullException.ThrowIfNull(liveFleetService);

        this.repository = repository;
        this.characterNameResolver = characterNameResolver;
        this.liveFleetService = liveFleetService;
    }

    public ObservableCollection<ProfileListItemViewModel> ProfileItems { get; } = [];

    public ObservableCollection<ProfileWingEditorViewModel> Wings { get; } = [];

    public ObservableCollection<ProfileAssignmentEditorViewModel> Assignments { get; } = [];

    public ObservableCollection<ProfileSquadOptionViewModel> SquadOptions { get; } = [];

    public ObservableCollection<string> UnresolvedRosterEntries { get; } = [];

    public DesiredRoleOptionViewModel[] RoleOptions { get; } =
    [
        new(DesiredFleetRole.SquadMember, "Squad member"),
        new(DesiredFleetRole.SquadCommander, "Squad commander"),
        new(DesiredFleetRole.WingCommander, "Wing commander"),
        new(DesiredFleetRole.FleetCommander, "Fleet commander"),
    ];

    public bool CanEdit => IsEditorActive && !IsBusy;

    public async Task InitializeAsync()
    {
        try
        {
            await ReloadProfilesAsync(null);
            StatusMessage = ProfileItems.Count == 0
                ? "No saved profiles yet. Create one or capture the current live fleet."
                : $"Loaded {ProfileItems.Count} saved profile{(ProfileItems.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Profiles could not be loaded: {exception.Message}";
        }
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

        var answer = MessageBox.Show(
            $"Delete profile '{ProfileName}'?\n\nThis removes only the saved local profile. It does not change the EVE fleet.",
            "Delete profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await repository.DeleteAsync(EditingProfileId);
            await ReloadProfilesAsync(null);
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

        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".json",
            FileName = $"{GetSafeFileName(profile.Name)}.fleet-profile.json",
            Filter = "Fleet Organizer profile (*.json)|*.json|All files (*.*)|*.*",
            Title = "Export Fleet Organizer profile",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(
                dialog.FileName,
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

        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Fleet Organizer profile (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false,
            Title = "Import Fleet Organizer profile",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var json = await File.ReadAllTextAsync(dialog.FileName, Encoding.UTF8);
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
            RefreshValidation();
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
        RefreshValidation();
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
        RefreshValidation();
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
        RefreshValidation();
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
        RefreshValidation();
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
        if (Assignments.Any(assignment => squadIds.Contains(assignment.TargetSquadId)))
        {
            StatusMessage = "Move or remove characters assigned to this wing before deleting it.";
            return;
        }

        UnhookWing(wing);
        Wings.Remove(wing);
        UpdateSquadOptions();
        RefreshValidation();
    }

    [RelayCommand]
    private void DeleteSquad(ProfileSquadEditorViewModel? squad)
    {
        var wing = FindParentWing(squad);
        if (!CanEdit || squad is null || wing is null)
        {
            return;
        }

        if (Assignments.Any(assignment => assignment.TargetSquadId == squad.Id))
        {
            StatusMessage = "Move or remove characters assigned to this squad before deleting it.";
            return;
        }

        UnhookSquad(squad);
        wing.Squads.Remove(squad);
        UpdateSquadOptions();
        RefreshValidation();
    }

    [RelayCommand]
    private void SelectAllAssignments()
    {
        foreach (var assignment in Assignments)
        {
            assignment.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearAssignmentSelection()
    {
        foreach (var assignment in Assignments)
        {
            assignment.IsSelected = false;
        }
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
    }

    partial void OnSelectedProfileChanged(ProfileListItemViewModel? value)
    {
        if (value is null)
        {
            ClearEditor();
            return;
        }

        LoadEditor(value.Profile);
    }

    partial void OnProfileNameChanged(string value)
    {
        _ = value;
        RefreshValidation();
    }

    private async Task ReloadProfilesAsync(Guid? selectedProfileId)
    {
        var profiles = await repository.LoadAllAsync();
        ProfileItems.Clear();
        foreach (var profile in profiles)
        {
            ProfileItems.Add(new ProfileListItemViewModel(profile));
        }

        SelectedProfile = selectedProfileId is Guid id
            ? ProfileItems.FirstOrDefault(item => item.Id == id)
            : ProfileItems.FirstOrDefault();
        if (SelectedProfile is null)
        {
            ClearEditor();
        }
    }

    private void LoadEditor(FleetProfile profile)
    {
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

        UnresolvedRosterEntries.Clear();
        RosterPasteText = string.Empty;
        IsEditorActive = true;
        UpdateSquadOptions();
        isLoadingEditor = false;
        RefreshValidation();
    }

    private void ClearEditor()
    {
        isLoadingEditor = true;
        ClearEditorCollections();
        EditingProfileId = Guid.Empty;
        ProfileName = string.Empty;
        IsEditorActive = false;
        SquadOptions.Clear();
        UnresolvedRosterEntries.Clear();
        ValidationSummary = "No profile selected.";
        isLoadingEditor = false;
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

        Wings.Clear();
        Assignments.Clear();
    }

    private FleetProfile BuildCurrentProfile() =>
        new(
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
            }).ToArray());

    private void RefreshValidation()
    {
        if (isLoadingEditor || !IsEditorActive)
        {
            return;
        }

        var errors = ProfileValidator.Validate(BuildCurrentProfile());
        ValidationSummary = errors.Count == 0
            ? $"Valid profile • {Assignments.Count} characters • {Wings.Count} wings • {SquadOptions.Count} squads"
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

    private void OnHierarchyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (string.Equals(e.PropertyName, "Name", StringComparison.Ordinal))
        {
            UpdateSquadOptions();
        }

        RefreshValidation();
    }

    private void OnAssignmentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        RefreshValidation();
    }

    private void MoveWing(ProfileWingEditorViewModel? wing, int offset)
    {
        if (!CanEdit || wing is null)
        {
            return;
        }

        MoveItem(Wings, wing, offset);
        RefreshValidation();
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
        RefreshValidation();
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

    private static string GetSafeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var safeCharacters = value
            .Select(character => invalidCharacters.Contains(character) ? '-' : character)
            .ToArray();
        var safeName = new string(safeCharacters).Trim();
        return safeName.Length == 0 ? "fleet-profile" : safeName;
    }
}
