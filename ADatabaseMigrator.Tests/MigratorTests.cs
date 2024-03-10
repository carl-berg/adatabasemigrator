﻿using ADatabaseMigrator.Hashing;
using ADatabaseMigrator.Journaling;
using ADatabaseMigrator.ScriptLoading.EmbeddedResources;
using ADatabaseMigrator.ScriptLoading.EmbeddedResources.Versioning;
using ADatabaseMigrator.Tests.Core;
using Dapper;
using Shouldly;

namespace ADatabaseMigrator.Tests;

public class MigratorTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Test_Migration()
    {
        using var connection = Fixture.CreateNewConnection();

        var migrator = new Migrator(
            scriptLoader: new EmbeddedResourceScriptLoader(new MD5ScriptHasher(), config => config
                .UsingAssemblyFromType<ScriptLoaderTests>()
                    .AddNamespaces<VersionFromPathVersionLoader>(MigrationScriptRunType.RunOnce, "Scripts.Migrations")
                    .AddNamespaces<VersionFromAssemblyVersionLoader>(MigrationScriptRunType.RunIfChanged, "Scripts.RunIfChanged")
                    .AddNamespaces<VersionFromAssemblyVersionLoader>(MigrationScriptRunType.RunAlways, "Scripts.RunAlways")),
            journalManager: new MigrationScriptJournalManager(connection),
            scriptRunner: new MigrationScriptRunner(connection));

        await migrator.Migrate(CancellationToken.None);

        var tables = await connection.QueryAsync<string>("SELECT table_name FROM INFORMATION_SCHEMA.TABLES");
        var journal = await connection.QueryAsync<SchemaVersionJournalDto>("SELECT Version, Name, Hash, Type FROM SchemaVersionJournal");
        var runLogEntries = await connection.QuerySingleAsync<int>("SELECT COUNT(1) FROM RunLog");

        await Verify(new { tables, journal, runLogEntries })
            .DontScrubGuids(/* Verify mistakes hashes for guids and scrubs them */);
    }

    [Fact]
    public async Task Test_Migration_Twice()
    {
        using var connection = Fixture.CreateNewConnection();

        var migrator = new Migrator(
            scriptLoader: new EmbeddedResourceScriptLoader(new MD5ScriptHasher(), config => config
                .UsingAssemblyFromType<ScriptLoaderTests>()
                    .AddNamespaces<VersionFromPathVersionLoader>(MigrationScriptRunType.RunOnce, "Scripts.Migrations")
                    .AddNamespaces<VersionFromAssemblyVersionLoader>(MigrationScriptRunType.RunIfChanged, "Scripts.RunIfChanged")
                    .AddNamespaces<VersionFromAssemblyVersionLoader>(MigrationScriptRunType.RunAlways, "Scripts.RunAlways")),
            journalManager: new MigrationScriptJournalManager(connection),
            scriptRunner: new MigrationScriptRunner(connection));

        await migrator.Migrate(CancellationToken.None);
        var secondRun = await migrator.Migrate(CancellationToken.None);
        secondRun.ShouldHaveSingleItem().Name.EndsWith("RunLog.sql");
    }

    [Fact]
    public async Task Test_RunIfChanged()
    {
        using var connection = Fixture.CreateNewConnection();

        var hasher = new MD5ScriptHasher();

        Migrator BuildMigratorWithScript(string script) => new(
            scriptLoader: new StaticScriptLoader(hasher, () => [("migration", script, MigrationScriptRunType.RunIfChanged, "1.0.0")]),
            journalManager: new MigrationScriptJournalManager(connection),
            scriptRunner: new MigrationScriptRunner(connection));

        var first_run = await BuildMigratorWithScript("SELECT 1").Migrate(CancellationToken.None);
        var second_run = await BuildMigratorWithScript("SELECT 1").Migrate(CancellationToken.None);
        var third_run = await BuildMigratorWithScript("SELECT 2").Migrate(CancellationToken.None);
        first_run.ShouldHaveSingleItem();
        second_run.ShouldBeEmpty();
        third_run.ShouldHaveSingleItem();

        var journal = await connection.QueryAsync<SchemaVersionJournalDto>("SELECT Version, Name, Hash, Type FROM SchemaVersionJournal");
        journal.ShouldSatisfyAllConditions(
            j => j.Count().ShouldBe(2),
            j => j.ShouldAllBe(x => x.Version == "1.0.0"),
            j => j.ShouldAllBe(x => x.Name == "migration"),
            j => j.ShouldAllBe(x => x.Type == MigrationScriptRunType.RunIfChanged));
    }

    private record SchemaVersionJournalDto(string Version, string Name, string Hash, MigrationScriptRunType Type);

    private class StaticScriptLoader(IScriptHasher scriptHasher, Func<IReadOnlyList<(string Name, string Script, MigrationScriptRunType Type, string Version)>> loadScripts) : EmbeddedResourceScriptLoader(scriptHasher)
    {
        public override Task<IReadOnlyList<MigrationScript>> Load(CancellationToken? cancellationToken = default)
        {
            var scripts = loadScripts()
                .Select(x => new MigrationScript(x.Name, x.Type, x.Version, x.Script, ScriptHasher.Hash(x.Script)))
                .ToList();

            return Task.FromResult<IReadOnlyList<MigrationScript>>(scripts);
        }
    }
}
