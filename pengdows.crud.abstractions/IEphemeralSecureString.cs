namespace pengdows.crud;

public interface IEphemeralSecureString : IDisposable
{
    string Reveal();
    void WithRevealed(Action<string> use);
}