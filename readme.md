# 🧩 Affinity Setter

Easily set and persist CPU affinity (and I/O priority!) for your processes on Linux.  
Make sure your apps run on the CPUs you want, every time! 🚀

---

## ⚡️ Usage

1. **Start the tool:** `sudo ./AffinitySetter`  
The first run will create an example config at `/etc/AffinitySetter.conf`

2. **Edit the config:**  
   Add or modify rules in `/etc/AffinitySetter.conf` to suit your needs.

3. **Add a new rule via command:**  
`sudo ./AffinitySetter save <RuleName> <CPUList>`
- `<RuleName>`: Any name you like (e.g., `MyAppRule`)
- `<CPUList>`: CPUs to use (e.g., `0-3,5`, `P`, `E`)

4. **View CPU topology:**  
`sudo ./AffinitySetter topology`

---

## 🛠 Features

- Set CPU affinity for processes and threads
- Persist your settings across reboots
- Set I/O priority (see config for options) ⚙️
- **Hybrid CPU support**: Automatic detection of P-cores (Performance) and E-cores (Efficiency)
- **Smart CPU keywords**: Use `P`, `E`, `physical`, `logical` instead of CPU numbers

---

## 🔥 Hybrid CPU Support (Intel 12th+ Gen / AMD Zen 4+)

AffinitySetter automatically detects your CPU topology and provides smart keywords:

| Keyword | Description |
|---------|-------------|
| `P`, `PCore` | All P-cores (Performance cores with HT) |
| `E`, `ECore` | All E-cores (Efficiency cores) |
| `P-physical` | P-cores physical threads only (no HT) |
| `P-logical`, `P-HT` | P-cores logical threads only (HT siblings) |
| `physical`, `no-HT` | All physical cores (first thread of each core) |
| `logical`, `HT` | All logical threads (HT siblings only) |
| `all` | All CPUs |

### Expressions

Use `+` to combine and `-` to exclude:
- `P+E` - All P-cores and E-cores (same as `all`)
- `all-logical` - All CPUs except HT siblings
- `P-P-HT` - P-cores without HT (same as `P-physical`)
- `physical+E` - Physical cores plus E-cores

### Example Config with Keywords

```json
[
  {
    "type": "name",
    "pattern": "game",
    "cpus": "P"
  },
  {
    "type": "name",
    "pattern": "browser",
    "cpus": "E"
  },
  {
    "type": "name",
    "pattern": "encoding",
    "cpus": "P-physical"
  },
  {
    "type": "name",
    "pattern": "background-task",
    "cpus": "all-logical"
  }
]
```

---

## 📝 Example Config

see [AffinitySetter.conf](./AffinitySetter.conf)

### 💡 Tips

- Edit `/etc/AffinitySetter.conf` to add more rules.
- Use `save` to quickly add new rules from the command line.
- Use `topology` command to see your CPU architecture.

That's all! 🎉

Questions or issues? Open an issue on GitHub!
