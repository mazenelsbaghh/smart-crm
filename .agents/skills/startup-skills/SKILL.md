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

### For Antigravity IDE (Workspace: `.agent/skills/`)
```bash
# Create the skills directory if it doesn't exist
mkdir -p .agent/skills

# Install github/spec-kit
git clone https://github.com/github/spec-kit.git .agent/skills/spec-kit

# Install pbakaus/impeccable
npx skills add pbakaus/impeccable --path .agent/skills

# Install nextlevelbuilder/ui-ux-pro-max-skill
git clone https://github.com/nextlevelbuilder/ui-ux-pro-max-skill.git .agent/skills/ui-ux-pro-max-skill
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
git clone https://github.com/nextlevelbuilder/ui-ux-pro-max-skill.git .claude/skills/ui-ux-pro-max-skill
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
git clone https://github.com/nextlevelbuilder/ui-ux-pro-max-skill.git .cursor/skills/ui-ux-pro-max-skill
```
