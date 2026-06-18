using FourRVivi.Core.Game;
using FourRVivi.Core.Memory;

namespace FourRVivi.Core.Signatures;

public sealed class BindResult
{
    public bool Matched { get; set; }
    public List<string> Bound { get; } = new();
    public string Message { get; set; } = "";
}

/// <summary>On attach, identifies the client and auto-binds every role from a saved profile straight
/// into the GameSession's AddressBook. Everything downstream (HP reader, Discord, analytics) then works
/// with no manual scanning.</summary>
public sealed class SignatureBinder
{
    private readonly ProfileStore _store;
    public SignatureBinder(ProfileStore store) => _store = store;

    public BindResult TryAutoBind(GameSession session)
    {
        var res = new BindResult();
        if (!session.Reader.Attached) { res.Message = "Not attached."; return res; }

        string clientId = SignatureProfile.Identify(session.Reader);
        var profile = _store.Find(clientId);
        if (profile is null) { res.Message = "No profile for this client — pin one value to create it."; return res; }

        res.Matched = true;
        var scanner = new PointerScanner(session.Reader);
        foreach (var (role, binding) in profile.Roles)
        {
            try
            {
                var addr = scanner.Resolve(binding.Path);
                if (addr == IntPtr.Zero) continue;
                session.AddressBook.Set(role, new SavedAddress { Runtime = (long)addr, Type = binding.Type });
                res.Bound.Add(role);
            }
            catch { }
        }
        res.Message = res.Bound.Count > 0
            ? $"Auto-bound {string.Join(", ", res.Bound)} from profile ✓"
            : "Profile found but nothing resolved (client may have changed).";
        return res;
    }

    /// <summary>Build/extend a profile for the current client from a discovered pointer path.</summary>
    public void SaveBinding(GameSession session, string role, PointerPath path, string type, string displayName = "")
    {
        string clientId = SignatureProfile.Identify(session.Reader);
        if (string.IsNullOrEmpty(clientId)) return;
        var profile = _store.Find(clientId) ?? new SignatureProfile { ClientId = clientId, DisplayName = displayName };
        profile.Roles[role] = new RoleBinding { Path = path, Type = type };
        _store.Save(profile);
    }
}
