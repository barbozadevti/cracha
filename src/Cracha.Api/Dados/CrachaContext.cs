using System.Text.Json;
using Cracha.Api.Modelos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cracha.Api.Dados;

public abstract class CrachaContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Funcionario> Funcionarios => Set<Funcionario>();
    public DbSet<Departamento> Departamentos => Set<Departamento>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Ausencia> Ausencias => Set<Ausencia>();

    /// <summary>Histórico no próprio banco, usado quando não há Azure Table configurada.</summary>
    public DbSet<RegistroHistorico> Historico => Set<RegistroHistorico>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Departamento>(departamento =>
        {
            departamento.Property(d => d.Nome).HasMaxLength(40).IsRequired();
            departamento.Property(d => d.Cor).HasMaxLength(7).IsRequired();
            departamento.HasIndex(d => d.Nome).IsUnique();
        });

        modelBuilder.Entity<Funcionario>(funcionario =>
        {
            funcionario.Property(f => f.Nome).HasMaxLength(100).IsRequired();
            funcionario.Property(f => f.Cargo).HasMaxLength(60).IsRequired();
            funcionario.Property(f => f.Endereco).HasMaxLength(200).IsRequired();
            funcionario.Property(f => f.Ramal).HasMaxLength(4).IsRequired();
            funcionario.Property(f => f.EmailProfissional).HasMaxLength(120).IsRequired();
            funcionario.Property(f => f.Salario).HasPrecision(12, 2);
            funcionario.Property(f => f.Situacao).HasConversion<string>().HasMaxLength(10);
            funcionario.HasIndex(f => f.EmailProfissional).IsUnique();
            funcionario.HasIndex(f => f.Nome);
            funcionario.HasOne(f => f.Departamento)
                .WithMany(d => d.Funcionarios)
                .HasForeignKey(f => f.DepartamentoId)
                .OnDelete(DeleteBehavior.Restrict);
            // Autorrelacionamento do organograma: o controller cuida dos subordinados antes de remover.
            funcionario.HasOne(f => f.Gestor)
                .WithMany(g => g.Subordinados)
                .HasForeignKey(f => f.GestorId)
                .OnDelete(DeleteBehavior.Restrict);
            funcionario.Property(f => f.FotoVersao).HasMaxLength(32);
        });

        modelBuilder.Entity<Usuario>(usuario =>
        {
            usuario.Property(u => u.Nome).HasMaxLength(100).IsRequired();
            usuario.Property(u => u.Email).HasMaxLength(120).IsRequired();
            usuario.Property(u => u.SenhaHash).HasMaxLength(200).IsRequired();
            usuario.Property(u => u.Perfil).HasConversion<string>().HasMaxLength(15);
            usuario.Property(u => u.Carimbo).HasMaxLength(32).IsRequired();
            usuario.HasIndex(u => u.Email).IsUnique();
            usuario.HasOne(u => u.Funcionario)
                .WithMany()
                .HasForeignKey(u => u.FuncionarioId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Ausencia>(ausencia =>
        {
            ausencia.Ignore(a => a.Dias);
            ausencia.Property(a => a.Tipo).HasConversion<string>().HasMaxLength(10);
            ausencia.Property(a => a.Status).HasConversion<string>().HasMaxLength(10);
            ausencia.Property(a => a.Observacao).HasMaxLength(300);
            ausencia.Property(a => a.SolicitadaPor).HasMaxLength(100);
            ausencia.Property(a => a.DecididaPor).HasMaxLength(100);
            ausencia.Property(a => a.MotivoRecusa).HasMaxLength(300);
            ausencia.HasIndex(a => new { a.FuncionarioId, a.Inicio });
            ausencia.HasOne(a => a.Funcionario)
                .WithMany()
                .HasForeignKey(a => a.FuncionarioId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RegistroHistorico>(registro =>
        {
            registro.ToTable("Historico");
            registro.HasKey(r => r.Id);
            registro.Property(r => r.Id).HasMaxLength(32);
            registro.Property(r => r.NomeFuncionario).HasMaxLength(100);
            registro.Property(r => r.Departamento).HasMaxLength(40);
            registro.Property(r => r.Autor).HasMaxLength(100).HasDefaultValue("Sistema");
            registro.Property(r => r.TipoAcao).HasConversion<string>().HasMaxLength(15);
            registro.Property(r => r.Alteracoes).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<CampoAlterado>>(v, (JsonSerializerOptions?)null) ?? new List<CampoAlterado>(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<CampoAlterado>>(
                    (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                    v => v.ToList()));
            registro.HasIndex(r => r.FuncionarioId);
            registro.HasIndex(r => r.Quando);
        });
    }
}

// Um contexto por provedor, cada um com a sua pasta de migrations
// (Migrations/Sqlite e Migrations/SqlServer), já que o SQL gerado é diferente.

public sealed class CrachaSqliteContext(DbContextOptions<CrachaSqliteContext> options) : CrachaContext(options)
{
    // O SQLite não ordena DateTimeOffset gravado como texto; como número (ticks UTC) ele ordena.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
}

public sealed class CrachaSqlServerContext(DbContextOptions<CrachaSqlServerContext> options) : CrachaContext(options);

public sealed class FabricaSqliteDesign : IDesignTimeDbContextFactory<CrachaSqliteContext>
{
    public CrachaSqliteContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CrachaSqliteContext>().UseSqlite("Data Source=design.db").Options);
}

public sealed class FabricaSqlServerDesign : IDesignTimeDbContextFactory<CrachaSqlServerContext>
{
    public CrachaSqlServerContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CrachaSqlServerContext>().UseSqlServer("Server=localhost;Database=Cracha;Trusted_Connection=True;TrustServerCertificate=True").Options);
}
