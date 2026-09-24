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
        });

        modelBuilder.Entity<RegistroHistorico>(registro =>
        {
            registro.ToTable("Historico");
            registro.HasKey(r => r.Id);
            registro.Property(r => r.Id).HasMaxLength(32);
            registro.Property(r => r.NomeFuncionario).HasMaxLength(100);
            registro.Property(r => r.Departamento).HasMaxLength(40);
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
