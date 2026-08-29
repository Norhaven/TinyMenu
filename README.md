# TinyMenu

TinyMenu is a configuration-driven library for building console application menuing systems. Instead of writing code to define menus, prompts, and command execution flows, you describe your entire CLI experience in a single JSON configuration file. TinyMenu reads that file and drives the menu, prompts, and command execution for you.

This document describes the configuration schema, derived from an example configuration (`TinyMenu.Example.config.json`) that models a small kubectl-installer CLI.

## Top-level structure

A TinyMenu configuration file is a single JSON object with the following top-level sections:

```json
{
  "shellTypes": [...],
  "tools": [...],
  "environments": [...],
  "options": [...],
  "commands": [...],
  "menus": [...],
  "app": {...}
}
```

Each section is described below.

## shellTypes

Defines the shells TinyMenu can invoke commands through. Each entry has:

- `id` — a unique identifier used to reference this shell elsewhere in the config.
- `name` — a human-readable display name.
- `executable` — the executable TinyMenu launches (e.g. `cmd.exe`, `pwsh.exe`, `bash`).
- `invokeShellArgument` — optional. The flag passed to the shell executable to run a command string (e.g. `/C` for CMD, `-Command` for PowerShell). Shells that accept commands without a special flag, such as `bash`, can omit this.

Example:

```json
{
  "id": "powershell",
  "name": "PowerShell",
  "executable": "pwsh.exe",
  "invokeShellArgument": "-Command"
}
```

## tools

Declares external command-line tools that commands can invoke.

- `id` — unique identifier referenced by commands via `toolId`.
- `name` — human-readable display name.
- `command` — the actual executable to run (e.g. `curl.exe`, `kubectl.exe`, `winget.exe`).
- `appliesTo` — optional. Restricts availability of the tool. Uses the `appliesToPlatforms` type (see **appliesTo** below) to limit a tool to specific platforms, such as `winget` being Windows-only.

## environments

Defines named execution contexts that can be selected or targeted, bundling together platform, environment variables, and allowed shells.

- `id` — unique identifier referenced elsewhere via `appliesToEnvironments`.
- `name` — human-readable display name.
- `platform` — the target platform for this environment (e.g. `windows`).
- `variables` — a list of environment variables available in this context, each with a `name` and `value`.
- `shellTypes` — a list of shell type `id`s that are valid/available within this environment.

Example:

```json
{
  "id": "windows-debug",
  "name": "Windows (Debug)",
  "platform": "windows",
  "variables": [
    { "name": "DEBUG", "value": true }
  ],
  "shellTypes": ["powershell", "cmd"]
}
```

## options

Defines reusable, named selectable values — typically used as the choice list for a `getsUserInput` command (see below).

- `id` — unique identifier referenced by a command's `options` list.
- `name` — human-readable label shown to the user when selecting.
- `value` — the underlying value substituted when this option is chosen.

Example:

```json
{ "id": "64bit-option", "name": "64 bit", "value": "amd64" }
```

## commands

Commands are the reusable units of work in TinyMenu. Each command has an `id`, a `name`, an optional `appliesTo` restriction, and an `action` describing what it does. Commands can be composed — a command can invoke other commands by `id`, chain a sequence of steps, or branch by platform.

### Common command fields

- `id` — unique identifier, referenced by other commands (`commandId`) or by menu steps.
- `name` — human-readable display name.
- `appliesTo` — optional. Restricts when the command is available/applicable (see **appliesTo** below).
- `action` — the behavior of the command. The `type` field of the action determines which other fields are used.

### Action types

#### `getsUserInput`

Prompts the user for a value and stores it for later use in the flow.

- `prompt` — the text shown to the user.
- `captureAs` — the variable name the entered/selected value is stored under, referenced later via `{{capture:NAME}}` interpolation.
- `options` — optional. A list of `options` `id`s. When present, the prompt becomes a selection from these predefined choices rather than free-form text entry.

Example (free-form input):

```json
{
  "type": "getsUserInput",
  "prompt": "Enter the kubectl client version to download (e.g. v1.37.0):",
  "captureAs": "KUBECTL_CLIENT_VERSION"
}
```

Example (constrained selection):

```json
{
  "type": "getsUserInput",
  "prompt": "Select the processor architecture the client should have:",
  "captureAs": "PROCESSOR_ARCHITECTURE",
  "options": ["32bit-option", "64bit-option"]
}
```

#### `usesTool`

Invokes a declared tool with a set of arguments.

- `toolId` — the `id` of a tool from the `tools` section.
- `withArgs` — a list of argument strings passed to the tool's executable. Arguments may contain `{{capture:NAME}}` placeholders that are substituted with values captured earlier in the flow (from a `getsUserInput` step).

Example:

```json
{
  "type": "usesTool",
  "toolId": "curl",
  "withArgs": [
    "-LO",
    "https://dl.k8s.io/release/{{capture:KUBECTL_CLIENT_VERSION}}/bin/windows/{{capture:PROCESSOR_ARCHITECTURE}}/kubectl.exe"
  ]
}
```

#### `usesCommand`

Delegates to another previously defined command by reference.

- `commandId` — the `id` of the command to invoke.

Example:

```json
{ "type": "usesCommand", "commandId": "select-kubectl-client-version" }
```

#### `usesSteps`

Runs a sequence of steps in order, allowing composition of multiple commands/actions into a single command.

- `steps` — an ordered list of step objects, each with an `action` (any action type, typically `usesCommand`).

Example:

```json
{
  "type": "usesSteps",
  "steps": [
    { "action": { "type": "usesCommand", "commandId": "select-kubectl-client-version" } },
    { "action": { "type": "usesCommand", "commandId": "select-processor-architecture" } },
    { "action": { "type": "usesMultiPlatformCommands", "commands": [ /* ... */ ] } }
  ]
}
```

#### `usesMultiPlatformCommands`

Branches execution by platform, running a different action depending on the current platform.

- `commands` — a list of `{ "platform": "...", "action": {...} }` entries. TinyMenu selects the entry matching the active platform (as determined by the current environment) and runs its `action`.

Example:

```json
{
  "type": "usesMultiPlatformCommands",
  "commands": [
    {
      "platform": "windows",
      "action": {
        "type": "usesTool",
        "toolId": "winget",
        "withArgs": ["install pwsh"]
      }
    }
  ]
}
```

## menus

Defines the actual menu entries presented to the user. Each menu item is both a selectable option and a launcher for a sequence of steps.

- `fullOption` — the long-form flag/argument that selects this menu item from the command line (e.g. `-install-kube`).
- `shortOption` — the short-form flag/argument alias (e.g. `--ik`).
- `name` — internal identifier for the menu item.
- `description` — human-readable text describing what the menu item does, shown in the selection screen.
- `appliesTo` — optional. Restricts when this menu item is shown/available (see **appliesTo** below).
- `steps` — an ordered list of `{ "action": {...} }` entries executed when the menu item is chosen, following the same step/action structure as `usesSteps`.

Example:

```json
{
  "fullOption": "-install-kube",
  "shortOption": "--ik",
  "description": "Install a version of kubectl",
  "name": "install-kubectl",
  "appliesTo": {
    "type": "appliesToEnvironments",
    "environments": ["windows-debug"]
  },
  "steps": [
    { "action": { "type": "usesCommand", "commandId": "download-and-install-kubectl" } }
  ]
}
```

## appliesTo

A conditional filter that can be attached to `tools`, `commands`, and `menus` to restrict when they are available. It is an object with a `type` field determining its shape:

- `appliesToPlatforms` — restricts to specific platforms.
  - `platforms` — a list of platform identifiers (e.g. `["windows"]`).
- `appliesToEnvironments` — restricts to specific declared environments.
  - `environments` — a list of `environment` `id`s from the `environments` section.

Items without an `appliesTo` are assumed to be available in all platforms/environments.

## app

Global application-level settings controlling the look, feel, and behavior of the menu system.

- `name` — the display name of the CLI application.
- `description` — a short description of the application, shown to the user.
- `selectionColor` — the console color used to highlight the currently selected menu item.
- `defaultColor` — the console color used for unselected/default text.
- `selectionScreenHeader` — the header text displayed above the list of menu options.
- `shouldLog` — boolean. When `true`, TinyMenu logs its execution (command invocations, captured input, etc.) for diagnostic purposes.

Example:

```json
{
  "name": "Example CLI App",
  "description": "This is an example of the TinyMenu CLI app",
  "selectionColor": "Yellow",
  "defaultColor": "Gray",
  "selectionScreenHeader": "Please select one of:",
  "shouldLog": true
}
```

## Value interpolation

Anywhere a string value is used within an action's arguments (such as `withArgs`), TinyMenu supports interpolation of values captured earlier in the same execution flow via `getsUserInput` commands, using the syntax:

```
{{capture:VARIABLE_NAME}}
```

where `VARIABLE_NAME` matches a `captureAs` value from a prior `getsUserInput` step in the same flow.

## Putting it together: a complete flow

The example configuration models a single menu item, `install-kubectl`, that:

1. Is only shown when the active environment is `windows-debug`.
2. When selected, runs the `download-and-install-kubectl` command, which:
   - Prompts the user for a kubectl version (`select-kubectl-client-version`), capturing it as `KUBECTL_CLIENT_VERSION`.
   - Prompts the user to choose a processor architecture from a predefined option list (`select-processor-architecture`), capturing it as `PROCESSOR_ARCHITECTURE`.
   - Branches on platform (`usesMultiPlatformCommands`) and, on Windows, uses the `curl` tool to download the appropriate kubectl binary, substituting the two captured values into the download URL.

This illustrates the general TinyMenu pattern: declare your shells, tools, environments, and reusable options once; compose them into reusable commands (which may prompt for input, invoke tools, delegate to other commands, chain steps, or branch by platform); and finally expose entry points to those commands as menu items, each restricted to the environments or platforms where it makes sense.
