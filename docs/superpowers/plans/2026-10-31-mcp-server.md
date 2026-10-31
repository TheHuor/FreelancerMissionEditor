# MCP server implementation plan

Branch: codex/mcp-server, based on codex/bini-support / PR2. Previous changes remain separately reviewable; do not merge PR2 implicitly.

User authorization: implement external-agent MCP integration, no embedded/local model.

Architecture: separate net10.0 stdio executable using official C# SDK2.3.0 and MissionEditor.Core. Shared central MinVer. No HTTP endpoint, AI keys, model downloads or WPF dependency. Operates on saved files inside an explicitly configured workspace; unsaved editor buffers are outside this phase.

Tasks:
- [ ] Add isolated workspace service and tests for inspection, checks, source/context, missing-trigger evidence and suggestions.
- [ ] Add in-memory edit/new-mission drafts with preview and validation. Read-only by default; --allow-writes permits exporting drafts to new files only, never overwriting sources. SHA256 guards reject stale saved sources. Preserve BINI on edited copies.
- [ ] Expose bounded/paged MCP tools using stdio; all logging on stderr. Wire CLI options and clear error results.
- [ ] End-to-end MCP client test: initialize, list tools and invoke inspection; test writing boundaries, duplicate paths, invalid data and format preservation.
- [ ] Document build/start and client configuration; package/version the new module; build/test/review and push only this branch.

Missing-trigger suggestions are lexical candidates with calling references, not claims about the intended story. All checker findings retain explanations and limitations. New mission drafts are text INIs supplied by the agent, not automatically invented by the server.
