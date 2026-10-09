// Stores local profile identities; campaign saves will belong to each stable ID.
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

public sealed class PlayerProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

public static class ProfileStore
{
    #region Storage and Session
    private sealed class Index
    {
        public int Version { get; set; } = 1;
        public List<PlayerProfile> Profiles { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true
    };

    private static List<PlayerProfile> _profiles;
    public static PlayerProfile Selected { get; private set; }

    public static IReadOnlyList<PlayerProfile> Profiles
    {
        get { Load(); return _profiles.AsReadOnly(); }
    }

    private static string IndexPath =>
        ProjectSettings.GlobalizePath("user://Profiles/profiles.json");

    public static string RecoveryMessage { get; private set; } = "";
    #endregion

    #region Loading
    // =========================================================
    // Load once per run, recovering a valid backup without replacing damaged data.
    public static void Load()
    {
        if (_profiles != null) return;

        string path = IndexPath;

        if (!File.Exists(path) && !File.Exists(path + ".bak"))
        {
            _profiles = new();
            return;
        }

        try { _profiles = Read(path); }
        catch (Exception original)
        {
            try
            {
                _profiles = Read(path + ".bak");
                RecoveryMessage =
                    "Recovered profiles from the previous backup.";
            }
            catch
            {
                throw new IOException(
                    "Could not read profiles or their backup. " +
                    "Your existing files have been preserved. " +
                    original.Message, original);
            }
        }
    }

    // =========================================================
    // Reject unsupported formats and invalid identities before exposing profiles.
    private static List<PlayerProfile> Read(string path)
    {
        Index index = JsonSerializer.Deserialize<Index>(
            File.ReadAllText(path));

        if (index == null || index.Version != 1 ||
            index.Profiles == null || index.Profiles.Count > 1024)
            throw new InvalidDataException(
                "Invalid or unsupported profile index.");

        HashSet<string> ids = new(StringComparer.Ordinal);

        foreach (PlayerProfile profile in index.Profiles)
            if (profile == null ||
                !Guid.TryParseExact(profile.Id, "N", out _) ||
                !ids.Add(profile.Id) || !ValidName(profile.Name))
                throw new InvalidDataException("Invalid profile entry.");

        return index.Profiles;
    }
    #endregion

    #region Selection and Creation
    // =========================================================
    // Use IDs for identity; display names never become directory paths.
    public static void Select(string id)
    {
        Load();
        Selected = _profiles.FirstOrDefault(profile => profile.Id == id)
            ?? throw new ArgumentException(
                "That profile no longer exists.");
    }

    // =========================================================
    // Return to profile selection without changing any stored profile data.
    public static void ClearSelection() { Selected = null; }

    // =========================================================
    // Persist a new profile successfully before publishing it to the session.
    public static PlayerProfile Create(string name)
    {
        Load();
        name = (name ?? "").Trim();

        if (!ValidName(name))
            throw new ArgumentException(
                "Use 1–32 characters, without control characters.");

        if (_profiles.Any(p => string.Equals(
            p.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException(
                "A profile with that name already exists.");

        if (_profiles.Count >= 1024)
            throw new InvalidOperationException("Profile limit reached.");

        PlayerProfile profile = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            CreatedUtc = DateTime.UtcNow
        };

        List<PlayerProfile> updated = new(_profiles) { profile };
        Write(updated);
        _profiles = updated;
        Selected = profile;
        return profile;
    }

    // =========================================================
    // Keep profile names short and suitable for display.
    private static bool ValidName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Length <= 32 && !name.Any(char.IsControl);

    // =========================================================
    // Flush a temporary index and atomically replace the previous index.
    private static void Write(List<PlayerProfile> profiles)
    {
        string path = IndexPath, temporary = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        byte[] data = JsonSerializer.SerializeToUtf8Bytes(
            new Index { Profiles = profiles }, Json);

        using (FileStream stream = new(
            temporary, FileMode.Create,
            System.IO.FileAccess.Write, FileShare.None))
        {
            stream.Write(data);
            stream.Flush(true);
        }

        if (File.Exists(path))
        {
            // Keep a recovered backup instead of replacing it with bad data.
            string backup = string.IsNullOrEmpty(RecoveryMessage)
                ? path + ".bak" : null;

            File.Replace(temporary, path, backup);
        }
        else File.Move(temporary, path);

        RecoveryMessage = "";
    }
    #endregion
}