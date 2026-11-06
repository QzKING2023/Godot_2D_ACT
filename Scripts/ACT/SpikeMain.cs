using Godot;
using ACTSpike.ACT.Core;


namespace ACTSpike.ACT;

public partial class SpikeMain : Node2D
{
    private const float FloorY = 540f;
    private const float PerfectGuardSeconds = 0.25f;
    private Vector2 _player = new(260, 500);
    private Vector2 _enemy = new(900, 500);
    private Vector2 _velocity;
    private float _playerHp = 100f;
    private float _guard = 100f;
    private float _timeResource = 100f;
    private float _attackUntil;
    private float _dodgeUntil;
    private float _blockUntil;
    private float _slowUntil;
    private float _enemyTelegraphAt = 1.5f;
    private float _enemyAttackAt = -1f;
    private int _enemyHp = 6;
    private string _enemyState = "Idle";
    private string _lastEvent = "Ready";
    private bool _gameOver;

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        var now = Time.GetTicksMsec() / 1000f;
        if (Godot.Input.IsKeyPressed(Key.R)) ResetRound();
        if (_gameOver) { QueueRedraw(); return; }
        var slowed = now < _slowUntil && _timeResource > 0;
        if (slowed) _timeResource = Mathf.Max(0, _timeResource - dt * 18f);
        else _timeResource = Mathf.Min(100, _timeResource + dt * 8f);
        var simDt = slowed ? dt * 0.35f : dt;
        var axis = Godot.Input.GetAxis("move_left", "move_right");
        _velocity.X = Mathf.MoveToward(_velocity.X, axis * 300f, 1800f * dt);
        _velocity.Y += 1200f * simDt;
        if (Godot.Input.IsActionJustPressed("jump") && _player.Y >= FloorY) { _velocity.Y = -520; _lastEvent = "Jump"; }
        if (Godot.Input.IsActionJustPressed("attack")) Attack(now);
        if (Godot.Input.IsActionJustPressed("dodge")) { _dodgeUntil = now + 0.25f; _velocity.X = (axis == 0 ? 1 : Mathf.Sign(axis)) * 700f; _lastEvent = "Dodge"; }
        if (Godot.Input.IsActionPressed("block")) { _blockUntil = now + PerfectGuardSeconds; _guard = Mathf.Min(100, _guard + dt * 10f); _lastEvent = "Block"; }
        if (Godot.Input.IsActionJustPressed("time_slow") && _timeResource > 10) { _slowUntil = now + 1.5f; _lastEvent = "Time slow"; }
        _player += _velocity * dt; _player.X = Mathf.Clamp(_player.X, 40, 1240);
        if (_player.Y > FloorY) { _player.Y = FloorY; _velocity.Y = 0; }
        EnemyThink(now, simDt);
        QueueRedraw();
    }

    private void Attack(float now)
    {
        _attackUntil = now + 0.18f; _lastEvent = "Attack";
        if (_player.DistanceTo(_enemy) < 150 && Mathf.Abs(_player.Y - _enemy.Y) < 90) { _enemyHp--; _enemyState = _enemyHp <= 0 ? "Dead" : "Hit"; }
    }

    private void EnemyThink(float now, float dt)
    {
        if (_enemyHp <= 0) { _enemyState = "Dead"; return; }
        var distance = _player.DistanceTo(_enemy);
        if (distance > 180) { _enemyState = "Chase"; _enemy.X = Mathf.MoveToward(_enemy.X, _player.X, 90f * dt); }
        if (now >= _enemyTelegraphAt && _enemyAttackAt < 0) { _enemyState = "Telegraph"; _enemyAttackAt = now + 0.6f; _lastEvent = "Enemy warning"; }
        if (_enemyAttackAt >= 0 && now >= _enemyAttackAt) { ResolveEnemyAttack(now); _enemyAttackAt = -1; _enemyTelegraphAt = now + 2.5f; }
    }

    private void ResolveEnemyAttack(float now)
    {
        var grab = Mathf.FloorToInt(now) % 3 == 0;
        if (grab) { _lastEvent = "Grab: dodge required"; if (now > _dodgeUntil) _playerHp -= 22; }
        else if (now < _blockUntil) { _lastEvent = "Perfect guard"; _enemyState = "Hit"; }
        else if (now < _slowUntil || now < _dodgeUntil) _lastEvent = "Delayed attack avoided";
        else { _playerHp -= 15; _lastEvent = "Player hit"; }
        if (_playerHp <= 0) { _playerHp = 0; _gameOver = true; _lastEvent = "Player down - press R"; }
    }

    private void ResetRound()
    {
        _player = new(260, FloorY); _enemy = new(900, FloorY); _velocity = Vector2.Zero; _playerHp = 100; _guard = 100; _timeResource = 100; _enemyHp = 6; _enemyState = "Idle"; _enemyAttackAt = -1; _enemyTelegraphAt = Time.GetTicksMsec() / 1000f + 1.5f; _gameOver = false; _lastEvent = "Round reset";
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 1280, 720), new Color("101522")); DrawRect(new Rect2(0, FloorY + 40, 1280, 180), new Color("1b2b3a")); DrawLine(new Vector2(0, FloorY + 40), new Vector2(1280, FloorY + 40), Colors.LightBlue, 3);
        DrawCircle(_player, 34, new Color("55d6be")); DrawRect(new Rect2(_enemy - new Vector2(30, 55), new Vector2(60, 110)), _enemyHp > 0 ? new Color("ee6c4d") : Colors.Gray);
        var now = Time.GetTicksMsec() / 1000f; if (now < _attackUntil) DrawArc(_player, 70, 0, Mathf.Tau, 24, Colors.Yellow, 8); if (now < _blockUntil) DrawArc(_player, 52, -1.2f, 1.2f, 16, Colors.Cyan, 8); if (now < _dodgeUntil) DrawCircle(_player, 48, new Color(0.4f, 0.8f, 1, 0.25f));
        DrawString(ThemeDB.FallbackFont, new Vector2(32, 42), "ACT SPIKE | C# Combat Room", HorizontalAlignment.Left, -1, 26, Colors.White); DrawString(ThemeDB.FallbackFont, new Vector2(32, 78), "A/D move  Space jump  J attack  Shift dodge  Ctrl block  Q time slow  R restart", HorizontalAlignment.Left, -1, 18, Colors.LightGray); DrawString(ThemeDB.FallbackFont, new Vector2(32, 130), $"HP {_playerHp:0}   Guard {_guard:0}   Time {_timeResource:0}", HorizontalAlignment.Left, -1, 22, Colors.White); DrawString(ThemeDB.FallbackFont, new Vector2(32, 165), $"Enemy HP {_enemyHp} | {_enemyState} | {_lastEvent}", HorizontalAlignment.Left, -1, 20, Colors.Orange); DrawString(ThemeDB.FallbackFont, new Vector2(32, 680), $"Perfect guard window: {PerfectGuardSeconds:0.00}s (real timestamp)", HorizontalAlignment.Left, -1, 18, Colors.LightBlue);
    }
}

