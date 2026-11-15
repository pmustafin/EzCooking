---
description: "Use when writing or modifying C# code in EzCooking. Follow the repository's traditional C# formatting and file organization."
applyTo: "**/*.cs"
---
# C# coding style

- Always use braces for control-flow bodies, including single statements. Put braces and statements on separate lines.
- Follow the repository's `.editorconfig` and use consistent indentation and spacing. Keep statements readable rather than combining them on one line.
- Name private fields `_camelCase`, including static and readonly fields. Name constants in PascalCase.
- Put each top-level class or interface in its own appropriately named file. Records and enums may share files.
- Organize API code by feature in the existing feature folders, with namespaces matching their folders. Keep the composition root in `Program.cs` and database migration resources in `Migrations`.
- Preserve behavior when reorganizing code.
