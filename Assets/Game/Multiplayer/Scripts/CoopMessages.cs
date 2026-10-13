using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

// Named messages for everything that is not one player's own state: the world's events, enemy snapshots,
// hits and the run. The host hands its own sends straight to the handler (a host is never sent to itself).
public static class CoopMessages
{
    public const string RunStart = "loom.run", RunEnd = "loom.runEnd", Snapshots = "loom.snap", EnemyHit = "loom.enemyHit",
        Strike = "loom.strike", Event = "loom.event", Spawn = "loom.spawn", Flags = "loom.flags", FloorHash = "loom.hash",
        Countdown = "loom.countdown", Rules = "loom.rules", IntroBegin = "loom.intro", Finale = "loom.finale";

    static readonly Dictionary<string, Action<ulong, FastBufferReader>> handlers = new();

    static NetworkManager Net => NetworkManager.Singleton;

    public static void Register<T>(string name, Action<ulong, T> handler) where T : INetworkSerializable, new()
    {
        void Receive(ulong sender, FastBufferReader reader) { reader.ReadNetworkSerializable(out T value); handler(sender, value); }
        handlers[name] = Receive;
        Net.CustomMessagingManager.RegisterNamedMessageHandler(name, Receive);
    }

    public static void UnregisterAll()
    {
        if (Net != null && Net.CustomMessagingManager != null)
            foreach (var name in handlers.Keys) Net.CustomMessagingManager.UnregisterNamedMessageHandler(name);
        handlers.Clear();
    }

    static FastBufferWriter Write<T>(in T value) where T : INetworkSerializable
    {
        var writer = new FastBufferWriter(256, Allocator.Temp, 1 << 16);
        writer.WriteNetworkSerializable(value);
        return writer;
    }

    // To the host (or straight to the handler on the host).
    public static void ToHost<T>(string name, T value, NetworkDelivery delivery = NetworkDelivery.ReliableSequenced) where T : INetworkSerializable
    {
        if (Net == null || !Net.IsListening) return;
        if (Net.IsServer) { Local(name, value); return; }
        using var writer = Write(value);
        Net.CustomMessagingManager.SendNamedMessage(name, NetworkManager.ServerClientId, writer, delivery);
    }

    // Host only: to every guest but one (the one it came from).
    public static void ToGuests<T>(string name, T value, NetworkDelivery delivery = NetworkDelivery.ReliableSequenced, ulong except = ulong.MaxValue) where T : INetworkSerializable
    {
        if (Net == null || !Net.IsServer) return;
        using var writer = Write(value);
        foreach (var client in Net.ConnectedClientsIds)
        {
            if (client == NetworkManager.ServerClientId || client == except) continue;
            Net.CustomMessagingManager.SendNamedMessage(name, client, writer, delivery);
        }
    }

    // Host only: to one player (the host itself: straight to the handler).
    public static void ToClient<T>(string name, ulong client, T value, NetworkDelivery delivery = NetworkDelivery.ReliableSequenced) where T : INetworkSerializable
    {
        if (Net == null || !Net.IsServer) return;
        if (client == NetworkManager.ServerClientId) { Local(name, value); return; }
        using var writer = Write(value);
        Net.CustomMessagingManager.SendNamedMessage(name, client, writer, delivery);
    }

    static void Local<T>(string name, T value) where T : INetworkSerializable
    {
        if (!handlers.TryGetValue(name, out var handler)) return;
        using var writer = Write(value);
        using var reader = new FastBufferReader(writer, Allocator.Temp);
        handler(NetworkManager.ServerClientId, reader);
    }
}

// Messages that carry a list.
public struct SnapshotBatch : INetworkSerializable
{
    public int floorKey;
    public EnemySnapshot[] items;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref floorKey);
        int count = items != null ? items.Length : 0;
        s.SerializeValue(ref count);
        if (s.IsReader) items = new EnemySnapshot[count];
        for (int i = 0; i < count; i++) items[i].NetworkSerialize(s);
    }
}

public struct FlagSet : INetworkSerializable
{
    public string[] keys;
    public int[] values;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        int count = keys != null ? keys.Length : 0;
        s.SerializeValue(ref count);
        if (s.IsReader) { keys = new string[count]; values = new int[count]; }
        for (int i = 0; i < count; i++) { s.SerializeValue(ref keys[i]); s.SerializeValue(ref values[i]); }
    }
}

// The host's rules for the session (MultiplayerManager's switches), sent to each guest as it joins and when they change.
public struct CoopRules : INetworkSerializable
{
    public bool sharedLoot, hostAdventurer, partyProgression, cutaway;
    public float cutawayTiles;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref sharedLoot); s.SerializeValue(ref hostAdventurer); s.SerializeValue(ref partyProgression);
        s.SerializeValue(ref cutaway); s.SerializeValue(ref cutawayTiles);
    }
}

public struct CoopInt : INetworkSerializable
{
    public int a, b;
    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter { s.SerializeValue(ref a); s.SerializeValue(ref b); }
}
