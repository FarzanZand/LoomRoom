// Co-op: a scripted event room that plays the same for every player (CoopWorld.EventUsed, EventStep). Used: another
// player used one of its pieces (who is their stand-in here). Step: on a guest, the host's copy moved on.
public interface ICoopEventRoom
{
    void Used(int what, Character who);
    void Step(int step, int value, int seed);
}
