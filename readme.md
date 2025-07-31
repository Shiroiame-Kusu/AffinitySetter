# 🧩 Affinity Setter

Easily set and persist CPU affinity (and I/O priority!) for your processes on Linux.  
Make sure your apps run on the CPUs you want, every time! 🚀

---

## ⚡️ Usage

1. **Start the tool:** ```sudo ./AffinitySetter```  
The first run will create an example config at `/etc/AffinitySetter.conf`: ```AffinitySetter configuration```

2. **Edit the config:**  
   Add or modify rules in `/etc/AffinitySetter.conf` to suit your needs.

3. **Add a new rule via command:**  
```sudo ./AffinitySetter save <rulename> <cpulist></cpulist></rulename>```
- `<RuleName>`: Any name you like (e.g., `MyAppRule`)
    - `<CPUList>`: CPUs to use (e.g., `0-3,5`)

---

## 🛠 Features

- Set CPU affinity for processes and threads
- Persist your settings across reboots
- Set I/O priority (see config for options) ⚙️

---

## 📝 Example Config

see [AffinitySetter.conf](./AffinitySetter.conf)
### 💡 Tips

Edit `/etc/AffinitySetter.conf` to add more rules.
Use save to quickly add new rules from the command line.

That’s all! 🎉

Questions or issues? Open an issue on GitHub!