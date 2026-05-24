import pytest
import os
import subprocess
import shutil

@pytest.mark.asyncio
async def test_backup_and_restore_cycle():
    backup_script = "deploy/backup.sh"
    restore_script = "deploy/restore.sh"
    
    # 1. Assert scripts exist
    assert os.path.exists(backup_script), f"{backup_script} does not exist"
    assert os.path.exists(restore_script), f"{restore_script} does not exist"
    
    # Ensure they are executable
    os.chmod(backup_script, 0o755)
    os.chmod(restore_script, 0o755)

    # 2. Run backup script
    # Environment variables can configure destination
    backup_dir = "/tmp/smartcore_backups_test"
    if os.path.exists(backup_dir):
        shutil.rmtree(backup_dir)
    os.makedirs(backup_dir)

    os.environ["BACKUP_DIR"] = backup_dir
    
    result = subprocess.run([backup_script], capture_output=True, text=True)
    assert result.returncode == 0, f"Backup script failed: {result.stderr}"
    
    # Find generated backup archive
    files = os.listdir(backup_dir)
    assert len(files) > 0, "No backup file was generated"
    backup_file = os.path.join(backup_dir, files[0])
    assert backup_file.endswith(".tar.gz") or backup_file.endswith(".zip") or len(files) > 0

    # 3. Run restore script
    # Pick the generated archive and run restore
    restore_result = subprocess.run([restore_script, backup_file], capture_output=True, text=True)
    assert restore_result.returncode == 0, f"Restore script failed: {restore_result.stderr}"

    # Clean up test backups
    shutil.rmtree(backup_dir)
