﻿using ADatabaseMigrator.Core;

namespace ADatabaseMigrator
{
    public class MigrationScript : Migration, IMigrationScript
    {
        public MigrationScript(string id, MigrationScriptRunType runType, string script, string scriptHash) : base(id, runType, scriptHash)
        {
            Script = script;
        }

        /// <inheritdoc/>
        public string Script { get; }
    }
}
