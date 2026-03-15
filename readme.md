# 🧩 AffinitySetter

<div align="center">

**Automatic CPU Affinity Manager for Linux Processes**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Linux-FCC624?logo=linux&logoColor=black)](https://www.linux.org/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

*Pin your processes to specific CPU cores with Intel hybrid architecture support!*

[English](#-features) | [中文](#-功能特性)

</div>

---

## ✨ Features

- 🎯 **Automatic CPU affinity control** for processes and threads
- 🔄 **Hot reload** - Config changes take effect immediately
- ⚡ **I/O priority control** - Support realtime, best-effort, idle
- 🎮 **Nice priority adjustment** - Control process scheduling priority
- 🎛️ **Per-core frequency limits** - Configure min/max CPU frequency per CPU set
- 🔥 **Hybrid CPU support** - Auto-detect Intel 12th/13th/14th Gen P-cores and E-cores
- 📝 **Flexible matching rules** - Match by process name, path, or command line
- 🔍 **Regex support** - Use `/pattern/` for advanced matching

---

## 🚀 Quick Start

### Installation

```bash
# Download latest release or build from source
dotnet publish -c Release -r linux-x64

# Run (requires root for setting affinity)
sudo ./AffinitySetter
```

### Basic Usage

```bash
# Start service (uses default config /etc/AffinitySetter.conf)
sudo ./AffinitySetter

# Use custom config file
sudo ./AffinitySetter load /path/to/config.json

# View CPU topology
./AffinitySetter topology

# Quick add rules
sudo ./AffinitySetter save firefox P
sudo ./AffinitySetter save chrome E

# Show help
./AffinitySetter --help
```

---

## 🔥 Hybrid CPU Architecture Support

AffinitySetter auto-detects CPU topology and provides smart CPU keywords:

### CPU Keywords

| Keyword | Description | Example (i9-13980HX) |
|---------|-------------|----------------------|
| `P`, `PCore` | All P-cores (with HT) | 0-15 |
| `E`, `ECore` | All E-cores | 16-31 |
| `P-physical` | P-core physical threads | 0,2,4,6,8,10,12,14 |
| `P-logical`, `P-HT` | P-core hyper-threads | 1,3,5,7,9,11,13,15 |
| `physical`, `no-HT` | All physical cores | 0,2,4,6,8,10,12,14,16-31 |
| `logical`, `HT` | All hyper-threads | 1,3,5,7,9,11,13,15 |
| `all` | All CPUs | 0-31 |

### Expression Syntax

Use `+` to combine, `-` to exclude:

```
P+E          # All cores (same as all)
all-logical  # All physical cores (exclude HT)
P-P-HT       # P-cores without HT
physical+E   # Physical cores + E-cores
```

### View CPU Topology

```bash
$ ./AffinitySetter topology

🔍 CPU Topology Detected:
   Total CPUs: 32 (0-31)
   Physical Cores: 24
   Logical Threads (HT): 8
   🚀 P-Cores (Performance): 16 (0-15)
      Physical: 8 (0,2,4,6,8,10,12,14)
      Logical (HT): 8 (1,3,5,7,9,11,13,15)
   🔋 E-Cores (Efficiency): 16 (16-31)
```

---

## 📝 Configuration

Config file is a JSON array. Each rule contains:

| Field | Required | Description |
|-------|----------|-------------|
| `type` | ✅ | Match type: `name` (process), `path` (executable), `cmdline` |
| `pattern` | ✅ | Match pattern, supports regex (wrap with `/`) |
| `cpus` | ✅ | CPU list: array `[0,1,2]`, range `"0-3"`, or keyword `"P"` |
| `iopriorityclass` | ❌ | I/O priority class: 1=realtime, 2=best-effort, 3=idle |
| `ioprioritydata` | ❌ | I/O priority data: 0-7 |
| `nice` | ❌ | Nice value: -20 to 19 |

Root object also supports `frequencyLimits`, used to set cpufreq limits through `/sys/devices/system/cpu/cpu*/cpufreq`.

| Field | Required | Description |
|-------|----------|-------------|
| `frequencyLimits[].cpus` | ✅ | CPU list or keyword, same syntax as `rules[].cpus` |
| `frequencyLimits[].minfreq` | ❌ | Minimum frequency in kHz |
| `frequencyLimits[].maxfreq` | ❌ | Maximum frequency in kHz |

### Example Config

```json
{
  "rules": [
    {
      "type": "name",
      "pattern": "game",
      "cpus": "P",
      "nice": -10
    },
    {
      "type": "name",
      "pattern": "browser",
      "cpus": "E",
      "iopriorityclass": 3,
      "ioprioritydata": 7
    },
    {
      "type": "cmdline",
      "pattern": "/--type=renderer/",
      "cpus": "E"
    },
    {
      "type": "path",
      "pattern": "/opt/myapp",
      "cpus": "0-7"
    },
    {
      "type": "name",
      "pattern": "encoding",
      "cpus": "P-physical"
    },
    {
      "type": "name",
      "pattern": "background",
      "cpus": [16, 17, 18, 19]
    }
  ],
  "frequencyLimits": [
    {
      "cpus": "P-physical",
      "minfreq": 2400000,
      "maxfreq": 5400000
    },
    {
      "cpus": "E",
      "maxfreq": 3600000
    }
  ]
}
```

Notes:

- `minfreq` and `maxfreq` use kHz, matching Linux cpufreq sysfs.
- Omitted values fall back to the startup defaults for that CPU.
- If the config root is still a legacy JSON array, it is treated as `rules` automatically.
- On platforms where several CPUs share the same cpufreq policy, one write may affect the whole policy group.

### Match Types

- **name**: Match process name from `/proc/[pid]/status` (max 15 chars)
- **path**: Match executable path from `/proc/[pid]/exe`
- **cmdline**: Match full command line from `/proc/[pid]/cmdline`

### CPU Format Support

```json
"cpus": [0, 1, 2, 3]      // Array format
"cpus": "0,1,2,3"         // Comma-separated
"cpus": "0-3"             // Range format
"cpus": "0-3,8-11"        // Mixed format
"cpus": "P"               // CPU keyword
"cpus": "P-physical"      // Expression
"cpus": "all-logical"     // Complex expression
```

---

## ⚙️ Command Line Options

```
Usage: AffinitySetter [options]

Options:
  --version, -v     Show version
  --help, -h        Show help
  --topology, -t    Show CPU topology
  load <file>       Use specified config file
  save <name> <cpu> Save a process rule
```

---

## 🏗️ Build from Source

```bash
# Clone repository
git clone https://github.com/Shiroiame-Kusu/AffinitySetter.git
cd AffinitySetter

# Build (requires .NET 10 SDK)
dotnet build

# Publish AOT version
dotnet publish -c Release -r linux-x64

# Binary location
./bin/Release/net10.0/linux-x64/publish/AffinitySetter
```

---

## 📋 Requirements

- **OS**: Linux (requires procfs and sysfs)
- **Permissions**: Root required for setting process affinity and I/O priority
- **Runtime**: None (AOT compiled to native executable)

---

## 🤝 Contributing

Issues and Pull Requests are welcome!

---

## 📄 License

[MIT License](LICENSE)

---

# 中文文档

## ✨ 功能特性

- 🎯 **自动设置进程/线程的 CPU 亲和性**
- 🔄 **配置热重载** - 修改配置文件后自动生效
- ⚡ **I/O 优先级控制** - 支持 realtime、best-effort、idle
- 🎮 **Nice 优先级调整** - 控制进程调度优先级
- 🔥 **混合架构支持** - 自动识别 Intel 12/13/14代 P-core 和 E-core
- 📝 **灵活的匹配规则** - 支持进程名、路径、命令行匹配
- 🔍 **正则表达式支持** - 使用 `/pattern/` 进行高级匹配

---

## 🚀 快速开始

### 安装

```bash
# 下载最新 Release 或从源码编译
dotnet publish -c Release -r linux-x64

# 运行（需要 root 权限设置亲和性）
sudo ./AffinitySetter
```

### 基本用法

```bash
# 启动服务（使用默认配置 /etc/AffinitySetter.conf）
sudo ./AffinitySetter

# 使用自定义配置文件
sudo ./AffinitySetter load /path/to/config.json

# 查看 CPU 拓扑信息
./AffinitySetter topology

# 快速添加规则
sudo ./AffinitySetter save firefox P
sudo ./AffinitySetter save chrome E

# 查看帮助
./AffinitySetter --help
```

---

## 🔥 混合 CPU 架构支持

AffinitySetter 自动检测 CPU 拓扑结构，提供智能 CPU 关键字：

### CPU 关键字

| 关键字 | 说明 | 示例 (i9-13980HX) |
|--------|------|-------------------|
| `P`, `PCore` | 所有 P 核心（含超线程） | 0-15 |
| `E`, `ECore` | 所有 E 核心 | 16-31 |
| `P-physical` | P 核心物理线程 | 0,2,4,6,8,10,12,14 |
| `P-logical`, `P-HT` | P 核心超线程 | 1,3,5,7,9,11,13,15 |
| `physical`, `no-HT` | 所有物理核心 | 0,2,4,6,8,10,12,14,16-31 |
| `logical`, `HT` | 所有超线程 | 1,3,5,7,9,11,13,15 |
| `all` | 所有 CPU | 0-31 |

### 表达式语法

使用 `+` 组合，`-` 排除：

```
P+E          # 所有核心（等同于 all）
all-logical  # 所有物理核心（排除超线程）
P-P-HT       # P 核心但不含超线程
physical+E   # 物理核心 + E 核心
```

---

## 📝 配置文件

配置文件为 JSON 数组格式，每个规则包含以下字段：

| 字段 | 必填 | 说明 |
|------|------|------|
| `type` | ✅ | 匹配类型：`name`（进程名）、`path`（路径）、`cmdline`（命令行） |
| `pattern` | ✅ | 匹配模式，支持正则（用 `/` 包围） |
| `cpus` | ✅ | CPU 列表：数组 `[0,1,2]`、范围 `"0-3"`、关键字 `"P"` |
| `iopriorityclass` | ❌ | I/O 优先级类：1=realtime, 2=best-effort, 3=idle |
| `ioprioritydata` | ❌ | I/O 优先级数据：0-7 |
| `nice` | ❌ | Nice 值：-20 到 19 |

### 配置示例

```json
[
  {
    "type": "name",
    "pattern": "game",
    "cpus": "P",
    "nice": -10
  },
  {
    "type": "name",
    "pattern": "browser",
    "cpus": "E",
    "iopriorityclass": 3,
    "ioprioritydata": 7
  },
  {
    "type": "cmdline",
    "pattern": "/--type=renderer/",
    "cpus": "E"
  }
]
```

### 匹配类型说明

- **name**: 匹配 `/proc/[pid]/status` 中的 Name 字段（进程名，最长 15 字符）
- **path**: 匹配 `/proc/[pid]/exe` 指向的可执行文件路径
- **cmdline**: 匹配 `/proc/[pid]/cmdline` 完整命令行

### CPU 格式支持

```json
"cpus": [0, 1, 2, 3]      // 数组格式
"cpus": "0,1,2,3"         // 逗号分隔
"cpus": "0-3"             // 范围格式
"cpus": "P"               // CPU 关键字
"cpus": "all-logical"     // 复杂表达式
```

---

## 📋 系统要求

- **操作系统**: Linux（需要 procfs 和 sysfs）
- **权限**: 需要 root 权限才能设置进程亲和性和 I/O 优先级
- **运行时**: 无需（AOT 编译为原生可执行文件）
