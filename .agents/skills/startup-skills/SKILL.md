---
name: startup-skills
description: "Initializes startup skills for a project: github/spec-kit, pbakaus/impeccable, and nextlevelbuilder/ui-ux-pro-max-skill."
risk: low
source: community
date_added: "2026-05-24"
---

# Startup Skills

Initializes the essential startup skills for the current project:
1. **GitHub Spec Kit** (https://github.com/github/spec-kit)
2. **Impeccable** (pbakaus/impeccable)
3. **UI/UX Pro Max Skill** (https://github.com/nextlevelbuilder/ui-ux-pro-max-skill)

## How to Apply

Run the following commands to install these skills in your project workspace.

### For Antigravity IDE (Workspace: `.agents/skills/`)
```bash
# Create the skills directory if it doesn't exist
mkdir -p .agents/skills

# Install and initialize github/spec-kit (requires uv to be installed)
git clone https://github.com/github/spec-kit.git .agents/skills/spec-kit
uv tool install --editable .agents/skills/spec-kit
# Ensure ~/.local/bin is in your PATH, or run:
~/.local/bin/specify init . --integration agy --force

# Install pbakaus/impeccable
npx skills add pbakaus/impeccable --path .agents/skills

# Install nextlevelbuilder/ui-ux-pro-max-skill (use CLI to generate files and fix root folder suffix)
npx uipro-cli init --ai antigravity
mv .agent/skills/ui-ux-pro-max .agents/skills/
rm -rf .agent
```

### For Claude Code (Workspace: `.claude/skills/`)
```bash
# Create the skills directory
mkdir -p .claude/skills

# Install github/spec-kit
git clone https://github.com/github/spec-kit.git .claude/skills/spec-kit

# Install pbakaus/impeccable
npx skills add pbakaus/impeccable --path .claude/skills

# Install nextlevelbuilder/ui-ux-pro-max-skill
npx uipro-cli init --ai claude
```

### For Cursor (Workspace: `.cursor/skills/`)
```bash
# Create the skills directory
mkdir -p .cursor/skills

# Install github/spec-kit
git clone https://github.com/github/spec-kit.git .cursor/skills/spec-kit

# Install pbakaus/impeccable
npx skills add pbakaus/impeccable --path .cursor/skills

# Install nextlevelbuilder/ui-ux-pro-max-skill
npx uipro-cli init --ai cursor
```
