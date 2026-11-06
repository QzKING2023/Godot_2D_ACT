using Godot;

namespace ACTSpike.ACT.Core;

public partial class RunSession : Node
{
    public bool HasPlayerState { get; private set; }
    public float PlayerHp { get; private set; } = 100f;

    public void SavePlayerHp(float hp)
    {
        PlayerHp = Mathf.Max(1f, hp);
        HasPlayerState = true;
    }

    public void Clear()
    {
        HasPlayerState = false;
        PlayerHp = 100f;
    }
}
