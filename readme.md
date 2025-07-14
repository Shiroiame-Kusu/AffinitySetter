## Affinity Setter
As it said, set CPU affinity for process and make it persistant.

## Usage
```
sudo ./AffinitySetter
```
And then it will start, pretty simple huh?
Actually, if you run it first time, it will trying to save example config to ```/etc/AffinitySetter.conf``` like 
```
# AffinitySetter configuration
# Format: process_substring:cpu_list
# Example:
# example:0-1
```
if you want to add more, just simply edit this config file or use
```
sudo ./AffinitySetter save Whateveryouwant 0-1024
```
Whateveryouwant means Whateveryouwant

0-1024 means the cpulist you want to use.

That's all.