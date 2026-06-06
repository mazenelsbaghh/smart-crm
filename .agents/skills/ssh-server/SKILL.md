---
name: "ssh-server"
description: "SSH Access & Operations on the remote production server (147.93.86.206)."
---

# SSH Server Management Skill

This skill provides credentials, connection guidelines, helper scripts, and typical administration tasks for managing the remote Hostinger server hosting the Smart Customer CRM production stack.

## Server & Connection Credentials

These credentials are saved locally in the project's root [.env.deploy](file://../../../../.env.deploy) file.

- **Host (IP)**: `147.93.86.206`
- **Username**: `root`
- **Password**: `MazenElsbagh.12`
- **Remote Project Directory**: `/root/smart-crm`

---

## Connection Tools & Helper Scripts

We provide wrapper scripts to simplify executing SSH commands without manually copying password strings.

### 1. Interactive Connection
To log in and open an interactive terminal session on the server, execute:
```bash
.agents/skills/ssh-server/scripts/ssh-interactive.sh
```

### 2. Remote Command Execution
To run any single command or script on the server and print the output locally, run:
```bash
.agents/skills/ssh-server/scripts/ssh-cmd.sh "<command>"
```
*Example:* `.agents/skills/ssh-server/scripts/ssh-cmd.sh "docker ps"`

---

## Common Administration & Troubleshooting Operations

Here are standard command patterns that you can run on the server via `ssh-cmd.sh` or directly:

### 1. Checking Containers Status
Check which containers are running, their status, ports, and health on the remote server:
```bash
.agents/skills/ssh-server/scripts/ssh-cmd.sh "docker ps -a"
```

### 2. Resolving Name Conflicts (e.g. Elasticsearch Container Conflict)
If deployment fails with an error stating a container name (e.g. `/smartcustomercore-elasticsearch`) is already in use by a dead or conflicting container, force-remove it:
```bash
# Force-remove conflicting containers
.agents/skills/ssh-server/scripts/ssh-cmd.sh "docker rm -f smartcustomercore-elasticsearch"
.agents/skills/ssh-server/scripts/ssh-cmd.sh "docker rm -f smartcustomercore-postgres"
.agents/skills/ssh-server/scripts/ssh-cmd.sh "docker rm -f smartcustomercore-redis"
.agents/skills/ssh-server/scripts/ssh-cmd.sh "docker rm -f smartcustomercore-rabbitmq"
.agents/skills/ssh-server/scripts/ssh-cmd.sh "docker rm -f smartcustomercore-minio"
.agents/skills/ssh-server/scripts/ssh-cmd.sh "docker rm -f smartcustomercore-backend"
.agents/skills/ssh-server/scripts/ssh-cmd.sh "docker rm -f smartcustomercore-frontend"
.agents/skills/ssh-server/scripts/ssh-cmd.sh "docker rm -f smartcustomercore-nginx"
```

### 3. Service Logs Tail
To monitor application and infrastructure logs:
- **Tail backend logs**:
  ```bash
  .agents/skills/ssh-server/scripts/ssh-cmd.sh "docker logs -f --tail=100 smartcustomercore-backend"
  ```
- **Tail all container logs**:
  ```bash
  .agents/skills/ssh-server/scripts/ssh-cmd.sh "cd /root/smart-crm && docker compose -f docker-compose.yml -f docker-compose.production.yml logs -f --tail=100"
  ```

### 4. Restarting the Stack
To restart the entire Docker Compose production services:
```bash
.agents/skills/ssh-server/scripts/ssh-cmd.sh "cd /root/smart-crm && docker compose -f docker-compose.yml -f docker-compose.production.yml restart"
```

### 5. Production Rebuild & Redeployment
Force rebuild and start all production services:
```bash
.agents/skills/ssh-server/scripts/ssh-cmd.sh "cd /root/smart-crm && docker compose -f docker-compose.yml -f docker-compose.production.yml down && docker compose -f docker-compose.yml -f docker-compose.production.yml up -d --build"
```

### 6. Cleaning Up Disk Space
Prune unused Docker data (dangling images, builder cache, stopped containers):
```bash
.agents/skills/ssh-server/scripts/ssh-cmd.sh "docker system prune -af"
```

### 7. PostgreSQL Database Queries
Execute direct SQL queries inside the postgres container:
```bash
.agents/skills/ssh-server/scripts/ssh-cmd.sh "docker exec -i smartcustomercore-postgres psql -U smartcore -d smartcustomercore -c 'SELECT COUNT(*) FROM \"Customers\";'"
```
