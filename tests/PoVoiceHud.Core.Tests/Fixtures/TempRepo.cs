using LibGit2Sharp;

namespace PoVoiceHud.Core.Tests.Fixtures;

/// <summary>A throwaway git repository with a small PMIS-like layout for provider and index tests.</summary>
public sealed class TempRepo : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "PoVoiceHud.Tests", Guid.NewGuid().ToString("N"));

    public TempRepo()
    {
        Directory.CreateDirectory(Root);
        Repository.Init(Root);
    }

    public static TempRepo WithSampleProject()
    {
        var repo = new TempRepo();
        repo.Write("src/Domain/Entities/Inspection.cs", """
            namespace Pmis.Domain;

            public class Inspection
            {
                public Guid Id { get; set; }
                public Guid TenantId { get; set; }
                public Tenant Tenant { get; set; } = null!;
            }
            """);
        repo.Write("src/Domain/Entities/Tenant.cs", "namespace Pmis.Domain;\n\npublic class Tenant { public Guid Id { get; set; } }\n");
        repo.Write("src/Services/InvoiceService.cs", "namespace Pmis.Services;\n\npublic class InvoiceService { }\n");
        repo.Write("src/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs", """
            using Microsoft.EntityFrameworkCore;

            namespace Pmis.Infrastructure.Migrations;

            partial class AppDbContextModelSnapshot : ModelSnapshot
            {
                protected override void BuildModel(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity("Pmis.Domain.Inspection", b =>
                        {
                            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uniqueidentifier");
                            b.Property<Guid>("TenantId").HasColumnType("uniqueidentifier");
                            b.Property<string>("Notes").HasMaxLength(2000).HasColumnType("nvarchar(2000)");
                            b.HasKey("Id");
                            b.HasIndex("TenantId");
                            b.ToTable("Inspections");
                        });

                    modelBuilder.Entity("Pmis.Domain.Tenant", b =>
                        {
                            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uniqueidentifier");
                            b.Property<string>("Name").IsRequired().HasMaxLength(200).HasColumnType("nvarchar(200)");
                            b.HasKey("Id");
                            b.ToTable("Tenants");
                        });

                    modelBuilder.Entity("Pmis.Domain.Inspection", b =>
                        {
                            b.HasOne("Pmis.Domain.Tenant", "Tenant")
                                .WithMany("Inspections")
                                .HasForeignKey("TenantId")
                                .OnDelete(DeleteBehavior.Cascade)
                                .IsRequired();
                        });
                }
            }
            """);
        repo.Write("docs/adr/0004-auth-lifecycle.md", """
            # ADR-0004: Auth Lifecycle

            Status: Accepted
            Deciders: platform team

            ## Context

            Services behind the gateway need the caller identity without re-validating tokens.

            ## Decision

            Use a ClaimsPrincipal header populated by the gateway (`src/Auth`).
            """);
        repo.Commit("Initial PMIS layout", new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));
        return repo;
    }

    public void Write(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public Commit Commit(string message, DateTimeOffset when, string author = "Jane Doe")
    {
        using var repo = new Repository(Root);
        Commands.Stage(repo, "*");
        var sig = new Signature(author, $"{author.Replace(' ', '.').ToLowerInvariant()}@example.com", when);
        return repo.Commit(message, sig, sig);
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Root, recursive: true);
    }
}
