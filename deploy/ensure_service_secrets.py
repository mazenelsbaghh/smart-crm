from pathlib import Path
import secrets


def ensure_service_secrets(env_file: Path) -> None:
    lines = env_file.read_text().splitlines()
    generated = []
    known_default = "a_very_long_and_secure_secret_key_that_is_at_least_32_characters_long"
    for name in ("JWT_SECRET", "WHATSAPP_GATEWAY_WEBHOOK_SECRET"):
        index = next((index for index, line in enumerate(lines) if line.startswith(name + "=")), None)
        current = lines[index].partition("=")[2].strip("\"' ") if index is not None else ""
        if len(current.encode()) >= 32 and current != known_default and "change-me" not in current.lower():
            continue
        replacement = name + "=" + secrets.token_hex(32)
        if index is None:
            lines.append(replacement)
        else:
            lines[index] = replacement
        generated.append(name)
    if generated:
        env_file.write_text("\n".join(lines) + "\n")
        print("Generated independent service secrets in .env: " + ", ".join(generated))
    env_file.chmod(0o600)


if __name__ == "__main__":
    ensure_service_secrets(Path(".env"))
