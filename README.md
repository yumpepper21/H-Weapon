# H-Weapon 🚀

**Minimalist, ad-free, and clean hardware detection tools.**

Welcome to **H-Weapon** — a suite of ultra-lightweight, single-file hardware diagnostic utilities built natively for Windows. 

Tired of bloatware, fake benchmark scores, and endless pop-up ads just to check your CPU temperature? We stripped away everything unnecessary. No background services, no telemetry, and no nonsense. Just raw, accurate hardware data at your fingertips.

---

## 📦 The Arsenal

H-Weapon is currently split into two focused, standalone modules:

### 🧠 [H-CPU](./H-CPU)
*   **Focus**: Processor monitoring and deep static specs.
*   **Features**: Real-time core temperatures, per-core load, current clock speeds, and detailed CPU architecture info (Cache, TDP, ID).
*   **Icon**: 🔴 Pixel-Art Red Chip

### 🎮 [H-GPU](./H-GPU)
*   **Focus**: Graphics card monitoring and VRAM tracking.
*   **Features**: Core temperature, GPU load, VRAM usage (e.g., MX450 2GB), and fan speeds (if supported). Auto-detects dual-GPU setups (Integrated + Dedicated).
*   **Icon**: 🟢 Pixel-Art Green Graphics Card

---

## ✨ Core Philosophy

*   **⚡ Blazing Fast**: Reads 100+ hardware parameters in just **1.4 seconds** using highly optimized multithreaded C# queries.
*   **🛡️ 100% Portable & Clean**: Single executable files (`.exe`). No installation, no registry bloat, no leftovers.
*   **🚫 Ad-Free & No Telemetry**: Your hardware data never leaves your machine. We don't track you, and we don't sell your data.
*   **🔍 Deep Hardware Access**: Uses Windows Management Instrumentation (WMI) and kernel-level drivers (`LibreHardwareMonitorLib`) for the most accurate sensor data.

---

## 💻 Tech Stack

*   **Language**: C# (.NET)
*   **UI Framework**: WinForms (Focused on extreme performance and zero UI lag)
*   **Hardware APIs**: WMI & `LibreHardwareMonitorLib`

---

## ⚠️ Important Notes (Must Read!)

1.  **Administrator Privileges**: To read deep sensor data (like CPU/GPU temperatures), these tools require **Administrator rights**. The UAC prompt is normal and necessary.
2.  **Antivirus False Positives**: Because these binaries are compiled by an independent developer **without a commercial code-signing certificate**, and they use kernel drivers to read hardware sensors, Windows Defender or other AVs might flag them as suspicious. Please allow them to run or add them to your whitelist.
3.  **Download**: Head to the **[Releases](YOUR_RELEASE_LINK_HERE)** page. Please download the `.7z` or `.zip` packages, extract them, and run the `.exe` directly.

---

## 🛠️ Build from Source

```bash
# Clone the repository
git clone https://github.com/yumpepper21/H-Weapon.git

# Open the solution in Visual Studio
# Build -> Publish -> Single-file executable (Self-contained)
