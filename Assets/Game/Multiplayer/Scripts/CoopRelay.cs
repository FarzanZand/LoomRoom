using System.Threading.Tasks;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

// Playing over the internet without opening ports: the host takes a Unity Relay allocation and gets a short join code;
// the others join with the code. Signs in anonymously (one profile per running game, so two copies on one computer
// are two players).
public static class CoopRelay
{
    static async Task SignIn()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            var options = new InitializationOptions();
            options.SetProfile($"p{System.Diagnostics.Process.GetCurrentProcess().Id}");
            await UnityServices.InitializeAsync(options);
        }
        if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    // The host: an allocation for this many guests, set on the transport. Returns the join code.
    public static async Task<string> Host(UnityTransport transport, int guests)
    {
        await SignIn();
        var allocation = await RelayService.Instance.CreateAllocationAsync(guests);
        var code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        transport.SetRelayServerData(allocation.ToRelayServerData("dtls"));
        return code;
    }

    public static async Task Join(UnityTransport transport, string code)
    {
        await SignIn();
        var allocation = await RelayService.Instance.JoinAllocationAsync(code.Trim().ToUpperInvariant());
        transport.SetRelayServerData(allocation.ToRelayServerData("dtls"));
    }

    // A join code is letters and digits only; anything with a dot or a colon is an address.
    public static bool LooksLikeCode(string text)
    {
        text = text != null ? text.Trim() : "";
        if (text.Length < 4 || text.Length > 12) return false;
        foreach (var c in text) if (!char.IsLetterOrDigit(c)) return false;
        return true;
    }
}
