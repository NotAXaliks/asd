using Microsoft.EntityFrameworkCore;

namespace Club.Core;

public class Game
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
}

public class Pc
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Zone { get; set; } = "";
    public decimal HourlyRate { get; set; }
    public string Status { get; set; } = "";
}

public class Session
{
    public int Id { get; set; }
    public int PcId { get; set; }
    public Pc Pc { get; set; } = null!;
    public int? GameId { get; set; }
    public Game? Game { get; set; }
    public string Player { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime EndsAt { get; set; }
    public DateTime? EndedAt { get; set; }
}

public class Payment
{
    public int Id { get; set; }
    public int? PcId { get; set; }
    public int? SessionId { get; set; }
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AuditEntry
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? PcId { get; set; }
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
    public string Source { get; set; } = "";
}

public class Player
{
    public int Id { get; set; }
    public string Nickname { get; set; } = "";
    public decimal Balance { get; set; }
}

public class TopUp
{
    public int Id { get; set; }
    public int PlayerId { get; set; }
    public decimal Amount { get; set; }
    public decimal Bonus { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ClubDb : DbContext
{
    // По умолчанию — локальный docker; для стенда и сервера строка задаётся в переменной окружения PRODAY_DB
    public static readonly string Conn = Environment.GetEnvironmentVariable("PRODAY_DB")
        ?? "Host=127.0.0.1;Port=40001;Database=proday;Username=xaliks;Password=coolPaSsw0rd;SSL Mode=Disable;Include Error Detail=true";

    public DbSet<Game> Games => Set<Game>();
    public DbSet<Pc> Pcs => Set<Pc>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<TopUp> TopUps => Set<TopUp>();

    protected override void OnConfiguring(DbContextOptionsBuilder o) =>
        o.UseNpgsql(Conn).UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Game>().ToTable("games");
        b.Entity<Pc>().ToTable("pcs");
        b.Entity<Session>().ToTable("sessions");
        b.Entity<Payment>().ToTable("payments");
        b.Entity<AuditEntry>().ToTable("audit_log");
        b.Entity<Player>().ToTable("players");
        b.Entity<TopUp>().ToTable("topups");
    }
}
