---
id: "11-8"
number: "11.8"
title: "Linux for backend developers"
part: 11
part_title: "Shipping software"
words: 10078
minutes: 44
---

# 11.8 Linux for backend developers

> **Quick look.** Enough Linux to run, debug and operate backend services.
>
> - Linux is a kernel; everything is a file; one tree from /, no drive letters.
> - Redirection and pipes compose small tools; > truncates the file before the command runs.
> - Quote variables and use -- before file names; test destructive commands with ls or find first.
> - Permissions: a directory without x blocks everything inside; the docker group equals root.
> - SIGTERM asks politely, SIGKILL doesn't; the OOM killer leaves evidence only in dmesg.
> - In containers bind to 0.0.0.0, not 127.0.0.1; check both host and cloud firewalls.
> - systemd runs services and journalctl reads their logs; cron has an almost empty environment.
> - Use LF line endings for scripts; SSH keys need 600 permissions.


### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Disk full, but no large files

`df` shows 100% used, `du` finds nothing big. The service keeps failing to write.

*A deleted file still held open*

```bash
df -h /var
sudo du -xh /var --max-depth=2 | sort -h | tail
sudo lsof +L1 | grep deleted          # a 40 GB log deleted while the process still writes to it
sudo systemctl restart bookings      # releases the space (or truncate via /proc/<pid>/fd/<n>)
```

> **Lesson:** Removing a file that a process holds open doesn't free space. Find it with lsof and restart or truncate.

#### Example 2: Running the API as a systemd service

A small deployment on a VM runs the API in a terminal session that dies on logout.

*/etc/systemd/system/bookings.service*

```ini
[Unit]
Description=Bookings API
After=network-online.target

[Service]
User=bookings
WorkingDirectory=/opt/bookings
ExecStart=/opt/bookings/Bookings.Api
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_HTTP_PORTS=5000
Restart=always
RestartSec=5
KillSignal=SIGTERM
TimeoutStopSec=30

[Install]
WantedBy=multi-user.target
```

*Operate it*

```bash
sudo systemctl daemon-reload && sudo systemctl enable --now bookings
journalctl -u bookings -f --since "10 min ago"
```

> **Lesson:** systemd starts, restarts and logs services for you; run as a dedicated non-root user.

### Ground floor

<p class="src">From <em>Backend Engineering</em> · Linux from Zero · Ground floor</p>

<p class="pd">What Linux actually is, what the shell is doing when you type, and the two ideas — everything is a file, everything is a process — that the rest of the system is built from.</p>

<a id="be-k1"></a>

#### What “Linux” actually means

Linux is a **kernel**: the program that owns the hardware. It decides which process runs on which CPU, hands out memory, and is the only thing allowed to talk to disks and network cards. On its own it is unusable — you never interact with it directly.

A **distribution** is the kernel plus everything that makes it a working system: a shell, core utilities, a package manager, an init system, libraries. Ubuntu, Debian, Alpine and Red Hat are distributions. When someone says “we run Linux”, they mean a distribution.

``` text
what you type              what runs it
──────────────────────────────────────────────────────
  you  →  shell (bash)          a normal program
          ↓ fork + exec
       your program             a normal program
          ↓ system call: read(), write(), socket()
       KERNEL                   the only thing with hardware access
          ↓
       hardware
```

| Distribution              | Where you meet it                             | Package manager |
|---------------------------|-----------------------------------------------|-----------------|
| **Ubuntu / Debian**       | Most servers, most container base images, WSL | `apt`           |
| **Alpine**                | Small container images (~5 MB)                | `apk`           |
| **RHEL / Rocky / Fedora** | Enterprise servers, regulated environments    | `dnf`           |
| **Debian slim**           | The usual .NET container base                 | `apt`           |

> **\[Trap\]**
>
> **Common trap.** Alpine uses **musl** instead of **glibc** as its C library. Most things work, but .NET needs the `-alpine` image variants specifically, and some native libraries do not build against musl at all. If a container works locally on Debian and dies on Alpine with a missing-symbol error, this is why.

> **\[Why it matters\]** Almost every backend you will ever deploy runs on Linux, including .NET ones. Every container image is a Linux filesystem. Understanding the system underneath is the difference between reading a production error and guessing at it.

<a id="be-k2"></a>

#### The shell: what happens when you press Enter

The shell is a program that reads a line, works out what you meant, and starts other programs. Bash is the usual one. It is not the operating system — it is a user-space program like any other, which is why you can replace it (zsh, fish) without changing anything else.

``` text
$ grep -i error app.log

1. bash SPLITS the line into words:  ["grep", "-i", "error", "app.log"]
2. bash EXPANDS anything special:    $VARS, *globs*, `commands`, ~
3. bash FINDS "grep" by walking $PATH directory by directory
4. bash FORKS itself, and the copy EXECs /usr/bin/grep
5. bash WAITS for it to exit, then shows the prompt again

everything after step 3 is a normal process — nothing magic
```

``` bash
# Where did that command come from?
which grep            # /usr/bin/grep   — first match in $PATH
type -a grep          # every match, plus aliases and shell builtins
command -v ls         # the portable version of "which"

# $PATH is just a colon-separated list of directories, searched left to right
echo $PATH
# /usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin

# Some things are NOT programs — they are built into the shell itself
type cd               # cd is a shell builtin
# (cd must be a builtin: a child process cannot change its parent's directory)

help cd               # help = docs for builtins
man grep              # man = manual for real programs   (q to quit, / to search)
grep --help           # usually the fastest answer
```

> **\[Trap\]**
>
> **Common trap.** `command not found` almost never means the software is missing — it means the shell could not find it in `$PATH`. Installed to `/opt/tool/bin`? The shell does not look there unless you tell it. And a `$PATH` exported in one shell does not exist in another, which is why “it works in my terminal but not in the systemd unit” happens constantly.

<a id="be-k3"></a>

#### Everything is a file

This is the design decision that explains most of Linux. Disks, terminals, network sockets, processes, kernel settings and random-number generators are all exposed as things you `read()` and `write()`. One set of verbs works on everything, so the same tools compose across wildly different resources.

``` bash
cat /proc/cpuinfo               # the CPU, as a text file
cat /proc/meminfo               # memory, as a text file
cat /proc/self/status           # facts about the process reading this
ls -l /proc/1234/fd             # every file THAT process has open
cat /sys/class/thermal/thermal_zone0/temp   # CPU temperature

head -c 16 /dev/urandom | xxd   # kernel randomness, as a file
echo hello > /dev/null          # the bin: writes vanish
                                # /dev/zero, /dev/full behave likewise
```

Every open file in a process is just a number, a **file descriptor**. Three are always there when a program starts, which is what makes pipes and redirection possible:

| FD  | Name                            | Default      |
|-----|---------------------------------|--------------|
| `0` | stdin — input                   | the keyboard |
| `1` | stdout — normal output          | the screen   |
| `2` | stderr — errors and diagnostics | the screen   |

> **\[Why it matters\]** Two of the most common production puzzles come straight from this: logs that vanish because the app wrote to stdout and nothing captured it, and “too many open files” errors, which are file-descriptor exhaustion — usually sockets or file handles never being closed.

<a id="be-k4"></a>

#### The filesystem: one tree, no drive letters

There is no `C:`. Everything hangs off a single root, `/`. Extra disks, USB sticks and network shares are **mounted** onto directories inside that one tree.

``` text
/                    the root of everything
├── bin  sbin  lib     core programs and libraries (often links into /usr)
├── boot                the kernel itself
├── dev                 devices: /dev/sda, /dev/null, /dev/urandom
├── etc                 CONFIGURATION. Plain text. You will live here.
│   ├── hosts           name → IP overrides
│   ├── fstab           what gets mounted at boot
│   └── systemd/system  your service definitions
├── home/nam            YOUR files
├── opt                 hand-installed third-party software
├── proc                the kernel + processes, as files (not on disk)
├── root                the root user's home (NOT the same as /)
├── srv                 data served by this machine
├── sys                 kernel and device settings, as files
├── tmp                 scratch. Wiped on reboot. World-writable.
├── usr                 the bulk of the OS: /usr/bin, /usr/lib, /usr/share
└── var                 things that CHANGE
    ├── log             log files          ← you will live here too
    └── lib             application state (databases, container images)
```

``` bash
pwd                       # where am I
cd /var/log               # absolute path — starts at /
cd ../lib                 # relative — .. is the parent
cd ~                      # home directory (also: just `cd`)
cd -                      # back to the previous directory

ls                        # names only
ls -l                     # long: permissions, owner, size, date
ls -lh                    # sizes humans can read (4.0K, 12M)
ls -la                    # include hidden files (names starting with .)
ls -lt                    # newest first — "what changed recently?"

df -h                     # free space per mounted filesystem
du -sh /var/log/*         # size of each thing in a directory
find / -xdev -size +500M 2>/dev/null   # what is eating the disk
```

> **\[Trap\]**
>
> **Common trap.** Linux filenames are **case-sensitive** and there is no file-extension magic: `Config.json` and `config.json` are different files. This is the single most common cause of “works on my Windows machine, 404s in the container” — an `appsettings.Production.json` that was committed as `appsettings.production.json`.

<a id="be-k5"></a>

#### Getting help, and staying oriented

``` text
man ls                 # full manual.  / to search, n for next, q to quit
man 5 crontab          # section 5 = file formats (section 1 = commands)
apropos permission     # search all manual pages by keyword
tldr tar               # community cheat sheets — often the fastest (install first)

history                # everything you have typed
Ctrl+R                 # search backwards through history — the key you will use most
!!                     # repeat the last command   (sudo !! is the classic)
!$                     # the last argument of the previous command

# essential line editing (these work in most prompts, not just bash)
Ctrl+A / Ctrl+E        # jump to start / end of line
Ctrl+W                 # delete the word before the cursor
Ctrl+U                 # clear the line
Ctrl+L                 # clear the screen
Ctrl+C                 # abandon the current command
Ctrl+D                 # end of input / log out
Tab                    # complete a name — press twice to list options
```

> **\[Trap\]**
>
> **Common trap.** `Ctrl+S` freezes the terminal (it is an ancient flow-control signal). It looks exactly like a hung machine. Press `Ctrl+Q` to unfreeze it.

### The shell as a tool

<p class="src">From <em>Backend Engineering</em> · Linux from Zero · The shell as a tool</p>

<p class="pd">Redirection, pipes, quoting and globbing. This is the part that turns a collection of small commands into a language for asking questions about a running system.</p>

<a id="be-k6"></a>

#### Redirection: pointing the three streams somewhere else

``` bash
command > file          # stdout INTO file, replacing it
command >> file         # stdout APPENDED to file
command < file          # file becomes stdin
command 2> errors.log   # stderr only (fd 2)
command > all.log 2>&1  # stdout to file, then stderr to the SAME place
command &> all.log      # bash shorthand for the line above
command 2>/dev/null     # throw errors away — useful with find and grep

# order matters, and this is the classic mistake:
command 2>&1 > out.log  # WRONG: stderr goes to the OLD stdout (screen),
                        # because 2>&1 was evaluated before the redirect
command > out.log 2>&1  # RIGHT: redirect stdout first, then point stderr at it

# see output AND save it
dotnet run | tee app.log
dotnet run |& tee app.log     # including stderr
```

> **\[Trap\]**
>
> **Common trap.** `>` truncates the target the moment the line is parsed — before the command even runs. So `sort file.txt > file.txt` empties the file and then sorts nothing. Write to a new name, then move it into place.

<a id="be-k7"></a>

#### Pipes: composing small tools

A pipe connects one program’s stdout to the next one’s stdin. Both run at the same time, streaming — the second does not wait for the first to finish, which is why you can pipe a log file that is gigabytes long without loading it into memory.

``` bash
# "which IPs hit us most?" — five tools, one line
cat access.log | awk '{print $1}' | sort | uniq -c | sort -rn | head -10
#   ↑ read       ↑ first column   ↑ group  ↑ count  ↑ biggest  ↑ top 10

# the same shape answers most log questions:
grep ERROR app.log | cut -d' ' -f5- | sort | uniq -c | sort -rn | head
# → the most frequent distinct error messages

ps aux | grep dotnet             # find a process
ss -tulpn | grep 5000            # who is listening on port 5000
journalctl -u myapp | grep -i timeout | wc -l    # count timeouts

# exit status: 0 means success, anything else is failure
grep -q ERROR app.log && echo "found errors"     # && = run if success
mkdir -p /data || echo "could not create"        # || = run if failure
echo $?                                          # status of the last command
```

> **\[Trap\]**
>
> **Common trap.** In a pipeline, `$?` reports only the *last* command’s status. `false | true` succeeds. In scripts, use `set -o pipefail` so a failure anywhere in the pipeline fails the whole line — without it, a broken build step can pass silently.

<a id="be-k8"></a>

#### Quoting and globbing: where beginners get hurt

``` bash
# The shell expands * before the program ever sees it
ls *.log                # bash turns this into: ls a.log b.log c.log
ls /nope/*.log          # no match? bash passes the literal *.log through

# Quoting rules, in order of strictness
echo "$HOME/logs"       # double quotes: variables DO expand
echo '$HOME/logs'       # single quotes: nothing expands — literal $HOME
echo "it's $USER"       # double quotes handle apostrophes fine

# The rule that prevents most bugs: quote every variable
file="my report.txt"
rm $file                # BROKEN: tries to delete "my" and "report.txt"
rm "$file"              # correct

# Globs
*.log                   # any name ending .log
app-?.log               # exactly one character:  app-1.log
app-[0-9].log           # a character class
**/*.cs                 # recursive (needs: shopt -s globstar)

# Command substitution: use output as an argument
echo "today is $(date +%F)"
kill $(pgrep -f myapp)
files=$(find . -name '*.tmp')
```

> **\[Trap\]**
>
> **Common trap.** A file named `-rf` or a variable holding `--force` gets read as an *option*, not a filename. Use `--` to end option parsing: `rm -- "$file"`. This is also why `rm -rf $DIR/` with an empty `$DIR` is catastrophic — it becomes `rm -rf /`.

<a id="be-k9"></a>

#### Environment variables and where configuration comes from

``` text
echo $HOME $USER $PWD $SHELL     # already set for you
printenv                          # everything in the environment
export ASPNETCORE_ENVIRONMENT=Production   # set for THIS shell and its children
unset ASPNETCORE_ENVIRONMENT

# set for exactly one command — no leakage
ASPNETCORE_URLS=http://+:8080 dotnet MyApp.dll

# .NET reads env vars as configuration, with __ standing in for :
export ConnectionStrings__Default="Host=db;Database=app"
export Logging__LogLevel__Default=Warning
# → matches ConnectionStrings:Default in appsettings.json
```

``` text
where a login shell reads settings (bash):

/etc/profile           system-wide, login shells
~/.bash_profile        your login shell            ← SSH sessions
~/.bashrc              your interactive shells     ← new terminal tabs

the trap: a non-interactive shell (cron, systemd, a CI step, docker RUN)
reads NEITHER. Anything you put in ~/.bashrc simply does not exist there.
```

> **\[Trap\]**
>
> **Common trap.** Environment variables are inherited by child processes and visible in `/proc/PID/environ` to anyone who can read it. They are fine for configuration and acceptable for secrets in containers, but they are *not* a secret store: they leak into crash dumps, `ps` output in some forms, and any log line that dumps configuration.

### Files, users and permissions

<p class="src">From <em>Backend Engineering</em> · Linux from Zero · Files, users and permissions</p>

<p class="pd">Who can do what, and why your container cannot write to the directory you mounted. Permissions are the single most common cause of “it works locally” failures.</p>

<a id="be-k10"></a>

#### Reading a permission line

``` bash
$ ls -l /var/www/app
-rw-r--r--  1 nam  developers   4096 Aug 19 09:14 appsettings.json
drwxr-xr-x  3 nam  developers   4096 Aug 19 09:14 logs
-rwxr-xr-x  1 root root        18240 Aug 19 09:14 start.sh
│└┬┘└┬┘└┬┘  │  │     │
│ │  │  │   │  │     └─ group
│ │  │  │   │  └─ owner (user)
│ │  │  │   └─ number of links
│ │  │  └─ what EVERYONE ELSE can do
│ │  └─ what the GROUP can do
│ └─ what the OWNER can do
└─ type: - file   d directory   l symlink
```

|         | On a file           | On a directory                      |
|---------|---------------------|-------------------------------------|
| `r` (4) | read the contents   | list the names inside               |
| `w` (2) | modify the contents | create/delete/rename entries inside |
| `x` (1) | run it as a program | **enter it** / access things inside |

``` csharp
chmod 644 appsettings.json    # rw- r-- r--   owner writes, everyone reads
chmod 600 secrets.env         # rw- --- ---   owner only  ← use this for secrets
chmod 755 start.sh            # rwx r-x r-x   everyone can run it
chmod 700 /data/private       # owner only, including entering the directory

chmod +x deploy.sh            # symbolic form: add execute for everyone
chmod u+w,go-w file           # user gains write, group and others lose it
chmod -R g+rX /var/www        # recursive; capital X = execute on DIRECTORIES only

chown nam:developers file     # change owner and group
chown -R app:app /var/www/app # what you do after copying files as root
```

> **\[Trap\]**
>
> **Common trap.** A directory without `x` is a wall: you cannot reach anything inside it, no matter how permissive the files are. `Permission denied` on a file whose permissions look fine is almost always a parent directory missing `x`. Check the whole path with `namei -l /var/www/app/config.json`.

<a id="be-k11"></a>

#### Users, groups, sudo and root

``` bash
whoami; id                    # who am I, and which groups am I in
sudo command                  # run one command as root
sudo -u app dotnet MyApp.dll  # run as a DIFFERENT user
sudo -i                       # a full root shell — avoid unless you must

# service accounts: no login, no home, no shell. This is how apps should run.
sudo useradd --system --no-create-home --shell /usr/sbin/nologin appuser
sudo usermod -aG docker nam   # add nam to the docker group
                              # (group changes need a new login to take effect)

# umask decides default permissions for NEW files
umask                         # 0022 → new files 644, new dirs 755
umask 0077                    # private by default: 600 / 700
```

> **\[Trap\]**
>
> **Common trap.** Adding a user to the `docker` group is equivalent to giving them root: they can start a container that mounts `/` and edit anything. It is convenient and it is a genuine privilege escalation — know that you are making that trade.

> **\[Why it matters\]** Containers run as root by default, which means a container escape starts with root on the host. Adding a non-root user to your Dockerfile is a two-line change, and it is the first thing an image scanner will flag.

<a id="be-k12"></a>

#### Links, and copying things around

``` csharp
cp file backup                     # copy
cp -r dir/ backup/                 # copy a directory
cp -a src/ dest/                   # archive: preserve permissions, times, links
mv old new                         # move OR rename — same operation
rm file;  rm -r dir;  rm -rf dir   # delete (there is no undo, no recycle bin)

mkdir -p /a/b/c                    # create the whole path, no error if it exists
touch file                         # create empty, or update the timestamp

ln -s /opt/app/current /var/www/app        # symlink: a pointer, can cross disks
ln /data/a.txt /data/b.txt                 # hard link: a second name for one file

# the deploy pattern symlinks make possible: atomic switch, instant rollback
ln -sfn /opt/releases/2026-08-19 /opt/app/current   # -n stops it nesting
```

> **\[Trap\]**
>
> **Common trap.** `rm` is immediate and total — no confirmation, no recycle bin, no undo. Two habits save you: run the destructive command as `ls` first to see what it matches, and never build a path by string concatenation in a script without checking the variable is non-empty (`${DIR:?not set}` aborts if it is).

### Processes and signals

<p class="src">From <em>Backend Engineering</em> · Linux from Zero · Processes and signals</p>

<p class="pd">How to see what is running, what it is doing, why it is using resources, and how to stop it without losing work.</p>

<a id="be-k13"></a>

#### Seeing what is running

``` bash
ps aux                       # every process, one line each
ps aux | grep [d]otnet       # the [d] trick stops grep matching itself
pgrep -af dotnet             # cleaner: PIDs plus full command lines
pstree -p                    # the parent/child tree

top                          # live view; press M = sort by memory, P = CPU, q = quit
htop                         # much nicer, if installed

# reading `ps aux` columns
# USER  PID  %CPU  %MEM   VSZ    RSS  STAT  START  TIME  COMMAND
#                          │      │     │
#          virtual (mostly meaningless)  │  actual RAM in KB   ← the one to read
#                                        └─ S sleeping, R running, D uninterruptible
#                                           Z zombie, T stopped

ls -l /proc/1234/exe         # exactly which binary a PID is running
cat /proc/1234/cmdline | tr '\0' ' '     # its full command line
ls /proc/1234/fd | wc -l     # how many files/sockets it has open
cat /proc/1234/status | grep -i threads
```

> **\[Trap\]**
>
> **Common trap.** State `D` (uninterruptible sleep) means the process is stuck in a kernel call, almost always waiting on I/O — a hung NFS mount or a failing disk. You *cannot* kill a `D` process, not even with `-9`. Fix the I/O.

<a id="be-k14"></a>

#### Signals: how you ask a process to stop

A signal is a small message the kernel delivers to a process. Two matter most, and confusing them is how deployments lose data.

| Signal         | Command       | Meaning                                                                                             |
|----------------|---------------|-----------------------------------------------------------------------------------------------------|
| `SIGTERM` (15) | `kill PID`    | **Please** shut down. The process can catch it, finish in-flight work, flush and exit. The default. |
| `SIGKILL` (9)  | `kill -9 PID` | The kernel destroys it. **Cannot be caught.** In-flight work is lost.                               |
| `SIGINT` (2)   | `Ctrl+C`      | Interrupt from the keyboard.                                                                        |
| `SIGHUP` (1)   | `kill -HUP`   | Terminal closed; many daemons repurpose it as “reload your config”.                                 |

``` text
kill 1234                # SIGTERM — always try this first
kill -9 1234             # SIGKILL — last resort, after TERM has failed
pkill -f "dotnet MyApp"  # by command line pattern
killall dotnet           # by program name

# jobs: running things in the background of your shell
long-task &              # start in the background
jobs                     # list background jobs
fg %1                    # bring job 1 to the foreground
Ctrl+Z                   # suspend the foreground job
bg                       # resume it in the background

# background work that survives you logging out
nohup ./long-task &> task.log &
tmux new -s work         # far better: a session you can detach and reattach
                         # Ctrl+B then D to detach, `tmux attach -t work` to return
```

> **\[Why it matters\]** This is exactly what happens on every deploy: the orchestrator sends SIGTERM, waits a grace period, then sends SIGKILL. If your application ignores SIGTERM, every deployment kills in-flight requests and drops queued work. The .NET host handles it for you — provided your background services honour the cancellation token.

<a id="be-k15"></a>

#### Resource limits and the OOM killer

``` bash
ulimit -a                    # limits for this shell
ulimit -n                    # max open file descriptors — the one that bites
free -h                      # memory: look at "available", not "free"
uptime                       # load average: 1, 5, 15 minutes
nproc                        # how many CPUs (load 4.0 on 4 cores = fully busy)

# was my process killed by the kernel, or did it crash?
dmesg -T | grep -i "killed process"
journalctl -k | grep -i oom
```

``` text
"available" vs "free" memory

free -h
              total    used    free   shared  buff/cache   available
Mem:           16Gi   4.2Gi   0.4Gi    0.1Gi       11Gi        11Gi
                                ↑                    ↑            ↑
                     looks alarming      cached file data    what you can
                     — it is not         (reclaimable)       actually use

Linux uses spare RAM as disk cache on purpose. Empty RAM is wasted RAM.
Read "available". Panicking about low "free" is a rite of passage.
```

> **\[Trap\]**
>
> **Common trap.** When memory runs out the kernel picks a process and kills it — the **OOM killer**. Your application simply disappears with no exception and no stack trace, and the only evidence is in `dmesg`. In containers the limit is the cgroup limit, not the host’s RAM, which is why a service dies at 512 MB on a machine with 64 GB free.

### Text and data tools

<p class="src">From <em>Backend Engineering</em> · Linux from Zero · Text and data tools</p>

<p class="pd">The tools that answer questions about logs and files. Learning four of these properly replaces most ad-hoc scripting, and they are the fastest debugging instrument on any server.</p>

<a id="be-k16"></a>

#### grep: finding things

``` bash
grep ERROR app.log                 # lines containing ERROR
grep -i error app.log              # case-insensitive
grep -v DEBUG app.log              # INVERT: everything except DEBUG
grep -c ERROR app.log              # just count the matches
grep -n ERROR app.log              # show line numbers
grep -r "ConnectionString" ./src   # recursive through a directory
grep -l TODO *.cs                  # only the NAMES of matching files

# context is what makes grep useful on stack traces
grep -A 20 "Unhandled exception" app.log   # 20 lines AFTER each match
grep -B 5  "Timeout" app.log               # 5 lines BEFORE
grep -C 3  "ERROR" app.log                 # 3 lines either side

grep -E "ERROR|FATAL|CRITICAL" app.log     # -E = extended regex, alternation
grep -F "1.2.3.4" access.log               # -F = fixed string, no regex, faster
grep -o "GET /[a-z/]*" access.log          # print only the matching part

# following a live log, filtered
tail -f app.log | grep --line-buffered -i error
```

> **\[Trap\]**
>
> **Common trap.** Without `--line-buffered`, `grep` buffers its output when writing to a pipe, so `tail -f | grep | tee` appears to show nothing for minutes. Everything is working; you are just watching a 4 KB buffer fill.

<a id="be-k17"></a>

#### sed and awk: transforming and summarising

`sed` edits a stream line by line. `awk` splits each line into fields and lets you compute over them. You do not need to master either — five patterns cover almost everything you will do on a server.

``` text
# sed — substitute
sed 's/localhost/db.internal/' app.conf          # first match per line
sed 's/localhost/db.internal/g' app.conf         # every match (g = global)
sed -i.bak 's/8080/5000/g' docker-compose.yml    # edit IN PLACE, keeping a .bak
sed -n '100,120p' huge.log                       # print only lines 100-120
sed '/^#/d; /^$/d' config.ini                    # strip comments and blank lines

# awk — fields. $1 is the first column, $NF the last, $0 the whole line
awk '{print $1}' access.log                      # first column
awk -F: '{print $1, $7}' /etc/passwd             # -F sets the separator
awk '$9 == 500 {print $7}' access.log            # URLs that returned 500
awk '{sum += $10} END {print sum/1024/1024 " MB"}' access.log
awk '{count[$1]++} END {for (ip in count) print count[ip], ip}' access.log \
  | sort -rn | head
```

``` text
# the supporting cast
cut -d',' -f1,3 data.csv       # columns 1 and 3 of a CSV
sort -u names.txt              # sort and de-duplicate
sort -k2 -rn data.txt          # by column 2, numeric, descending
uniq -c                        # count adjacent duplicates (sort FIRST)
wc -l file                     # count lines
head -50 / tail -50            # first / last 50 lines
tail -f app.log                # follow live
tr -d '\r' < win.txt > unix.txt   # strip Windows line endings
column -t                      # align columns for reading
jq '.items[] | .name' data.json    # JSON, properly — not with grep
```

> **\[Trap\]**
>
> **Common trap.** `uniq` only collapses *adjacent* duplicates, so it is almost always wrong without a preceding `sort`. And `sed -i` without a suffix edits in place with no backup — on a config file with a bad regex, that is unrecoverable.

<a id="be-k18"></a>

#### find and xargs: acting on many files

``` csharp
find . -name "*.log"                    # by name (glob, quote it)
find . -iname "*.LOG"                   # case-insensitive
find /var/log -type f -mtime +30        # files modified more than 30 days ago
find . -type d -name bin                # directories named bin
find . -size +100M                      # bigger than 100 MB
find . -newer deploy.marker             # changed since a reference file
find . -name "*.tmp" -delete            # find and delete in one step

# run a command per result — -print0/-0 survives spaces in filenames
find . -name "*.cs" -print0 | xargs -0 grep -l "TODO"
find /var/log -name "*.gz" -mtime +90 -print0 | xargs -0 rm -f

# parallelism, for free
find . -name "*.png" -print0 | xargs -0 -P 4 -n 1 optipng
```

> **\[Trap\]**
>
> **Common trap.** Always run a destructive `find` without the action first — look at the list, then add `-delete`. A misplaced `-o` (or) changes what matches entirely, and `find / -name "*" -delete` does exactly what it says.

### Networking and remote access

<p class="src">From <em>Backend Engineering</em> · Linux from Zero · Networking and remote access</p>

<p class="pd">Enough networking to answer “is it listening, can I reach it, and who is holding the port” — plus SSH, which is how you will touch every server you ever operate.</p>

<a id="be-k19"></a>

#### Is it listening? Can I reach it?

``` bash
ss -tulpn                     # every listening socket (the modern netstat)
#  -t TCP  -u UDP  -l listening  -p process  -n numeric ports
ss -tulpn | grep :5000        # who owns port 5000
ss -s                         # socket summary — useful for TIME_WAIT pileups
lsof -i :5000                 # same question, different tool

ip addr                       # this machine's IP addresses  (old: ifconfig)
ip route                      # routing table — where does traffic go
cat /etc/resolv.conf          # which DNS servers this box uses
cat /etc/hosts                # local name overrides, checked BEFORE DNS

# is the name resolving?
dig +short api.example.com
getent hosts api.example.com  # resolves the way the SYSTEM does (hosts + DNS)

# can I reach the port? (ping only tests ICMP, which proves little)
nc -zv db.internal 5432       # TCP connect test — the right first check
curl -v https://api.example.com/health
curl -sS -o /dev/null -w "%{http_code} %{time_total}s\n" https://api/health
timeout 3 bash -c "</dev/tcp/db.internal/5432" && echo open   # no tools needed
```

``` text
"the service is down" — a checklist that terminates

1. is the process alive?          systemctl status myapp   /   ps aux
2. is it LISTENING?               ss -tulpn | grep 5000
3. on the right interface?        127.0.0.1:5000 is local-only —
                                  unreachable from outside the machine!
4. can I reach it locally?        curl localhost:5000/health
5. can I reach it remotely?       nc -zv host 5000   (from another box)
6. if 4 works and 5 does not →    firewall, security group, or the app
                                  bound to loopback only
```

> **\[Trap\]**
>
> **Common trap.** Binding to `127.0.0.1` inside a container makes the port unreachable from outside it, no matter how you publish it. For containers, .NET must bind to all interfaces: `ASPNETCORE_URLS=http://+:8080` or `http://0.0.0.0:8080`.

<a id="be-k20"></a>

#### SSH: keys, config and tunnels

``` bash
ssh-keygen -t ed25519 -C "nam@laptop"        # generate a key pair (once)
ssh-copy-id nam@server                        # install the PUBLIC key remotely
ssh nam@server                                # log in — no password prompt

scp file.txt nam@server:/tmp/                 # copy a file up
scp -r nam@server:/var/log/app ./logs         # and back down
rsync -avz --progress ./dist/ nam@server:/var/www/app/   # better for dirs:
                                              # resumable, only sends differences
```

``` bash
# ~/.ssh/config — stop typing hostnames and flags
Host prod
    HostName 10.0.4.12
    User deploy
    IdentityFile ~/.ssh/id_ed25519_prod
    ServerAliveInterval 30

Host db
    HostName 10.0.9.5
    User nam
    ProxyJump prod          # reach a private box THROUGH the bastion

# now:  ssh prod    /    ssh db

# port forwarding: reach a private database from your laptop
ssh -L 15432:db.internal:5432 prod
# → localhost:15432 on your machine IS db.internal:5432 in the VPC
#   point your local tooling at localhost:15432
```

> **\[Trap\]**
>
> **Common trap.** SSH refuses to use a private key whose permissions are too open — it must be `600`, and `~/.ssh` must be `700`. Copying keys through Windows or a shared drive resets this and produces the confusing `UNPROTECTED PRIVATE KEY FILE` error. Never copy the file ending in `.pub`’s partner — the private key — to a server; only the `.pub` goes remote.

<a id="be-k21"></a>

#### Firewalls and open ports

``` bash
sudo ufw status                     # Ubuntu's simple firewall
sudo ufw allow 22/tcp               # never lock yourself out — SSH first
sudo ufw allow 443/tcp
sudo ufw enable

sudo firewall-cmd --list-all        # RHEL family
sudo iptables -L -n -v              # the layer underneath both
```

> **\[Trap\]**
>
> **Common trap.** In cloud environments there are usually *two* firewalls: the host one above, and the provider’s security group / network ACL. A port opened in one and closed in the other is still closed. Check both before concluding the application is at fault — and remember Kubernetes adds a third layer with NetworkPolicies.

### Services, logs and packages

<p class="src">From <em>Backend Engineering</em> · Linux from Zero · Services, logs and packages</p>

<p class="pd">Running your application as a managed service, finding out what it said, and keeping the machine supplied with software.</p>

<a id="be-k22"></a>

#### systemd: making something a service

systemd is PID 1 on nearly every modern distribution: it starts things at boot, restarts them when they die, and collects their output. A **unit file** is how you describe your application to it.

``` csharp
# /etc/systemd/system/myapp.service
[Unit]
Description=My .NET API
After=network-online.target
Wants=network-online.target

[Service]
Type=notify                        # or simple; notify needs the systemd integration
WorkingDirectory=/var/www/myapp
ExecStart=/usr/bin/dotnet /var/www/myapp/MyApp.dll
Restart=always
RestartSec=5
KillSignal=SIGTERM                 # graceful shutdown — matches ASP.NET Core
TimeoutStopSec=30                  # how long to wait before SIGKILL
User=appuser
Group=appuser
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000
EnvironmentFile=/etc/myapp/secrets.env    # chmod 600, owned by appuser

# cheap hardening — a few lines that meaningfully reduce blast radius
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/var/www/myapp/data

[Install]
WantedBy=multi-user.target
```

``` bash
sudo systemctl daemon-reload      # ALWAYS after editing a unit file
sudo systemctl enable --now myapp # start it, and start it at boot
systemctl status myapp            # state, PID, memory, last few log lines
sudo systemctl restart myapp
sudo systemctl stop myapp
systemctl list-units --failed     # what is broken on this machine
systemctl cat myapp               # show the effective unit file
```

> **\[Trap\]**
>
> **Common trap.** Forgetting `daemon-reload` after editing a unit means systemd keeps running the old definition — you restart, nothing changes, and you start doubting reality. Also note `Restart=always` will happily restart a crash-looping app forever; set `StartLimitBurst` so a broken deploy fails loudly instead of hammering the database.

<a id="be-k23"></a>

#### journalctl: reading the logs

``` bash
journalctl -u myapp                   # everything this service has logged
journalctl -u myapp -f                 # follow live  (like tail -f)
journalctl -u myapp -n 200             # last 200 lines
journalctl -u myapp --since "10 min ago"
journalctl -u myapp --since "2026-08-19 09:00" --until "09:30"
journalctl -u myapp -p err             # priority: err and worse
journalctl -u myapp -o json-pretty     # structured output, for piping to jq
journalctl -k                          # kernel messages (OOM kills live here)
journalctl --disk-usage
sudo journalctl --vacuum-time=7d       # reclaim space

# classic files still exist for many services
tail -f /var/log/nginx/access.log
ls /var/log/                           # syslog, auth.log, dmesg …
```

> **\[Why it matters\]** Anything your application writes to stdout or stderr is captured here automatically when it runs under systemd — which is exactly why the containerised advice is “log to stdout, let the platform collect it”. Writing your own log files inside a container is how logs get lost on restart.

<a id="be-k24"></a>

#### Scheduled work: cron and timers

``` csharp
crontab -e                    # edit YOUR user's schedule
crontab -l                    # list it

# ┌ minute (0-59)
# │ ┌ hour (0-23)
# │ │ ┌ day of month (1-31)
# │ │ │ ┌ month (1-12)
# │ │ │ │ ┌ day of week (0-7, 0 and 7 = Sunday)
  0 3 * * *  /usr/local/bin/backup.sh >> /var/log/backup.log 2>&1
  */5 * * * * /usr/bin/curl -sf https://api/health || echo "down" | logger
  0 */6 * * * /usr/bin/find /tmp -mtime +1 -delete
```

> **\[Trap\]**
>
> **Common trap.** cron runs with a nearly empty environment — a different `$PATH`, no `~/.bashrc`, often `/bin/sh` rather than bash, and a different working directory. Scripts that work in your terminal fail silently at 3am. Use absolute paths for everything, redirect output to a log, and test with `env -i /bin/sh -c 'your command'`.

``` text
# systemd timers: the modern alternative — logs to the journal, survives
# missed runs (Persistent=true), and can depend on other units.
# /etc/systemd/system/backup.timer
[Timer]
OnCalendar=daily
Persistent=true
[Install]
WantedBy=timers.target
# `systemctl list-timers` shows the next run for everything on the box
```

<a id="be-k25"></a>

#### Installing software

``` bash
# Debian / Ubuntu
sudo apt update                     # refresh the package lists FIRST
sudo apt install -y curl jq htop
apt list --installed | grep dotnet
sudo apt upgrade                    # upgrade everything installed
apt-cache policy dotnet-sdk-9.0     # which versions are available

# RHEL family
sudo dnf install curl jq

# Alpine
apk add --no-cache curl jq

# what does this command belong to?
dpkg -S $(which dotnet)             # Debian/Ubuntu
rpm -qf $(which dotnet)             # RHEL
```

> **\[Trap\]**
>
> **Common trap.** `apt install` without a preceding `apt update` often fails with 404s, because the cached package index points at versions that have since been replaced. In Dockerfiles the two must be in the *same* `RUN` layer, followed by `rm -rf /var/lib/apt/lists/*` — otherwise a cached update layer goes stale and the build breaks weeks later.

### Running .NET on Linux

<p class="src">From <em>Backend Engineering</em> · Linux from Zero · Running .NET on Linux</p>

<p class="pd">The specific differences that catch Windows-trained .NET developers, and a diagnosis routine for when a service misbehaves on a machine you cannot attach a debugger to.</p>

<a id="be-k26"></a>

#### The differences that actually bite

| Windows habit                 | On Linux                                                                                 |
|-------------------------------|------------------------------------------------------------------------------------------|
| `Config.json` ≡ `config.json` | **Case-sensitive.** The number-one cause of “file not found” in containers               |
| `path\to\file`                | `path/to/file`. Always build paths with `Path.Combine`                                   |
| `C:\ProgramData\app`          | `/var/lib/app`, and you must create it and chown it                                      |
| Runs as your logged-in user   | Runs as a service account with no home directory and no write access anywhere by default |
| CRLF line endings             | LF. A shell script with CRLF fails with a cryptic `bad interpreter`                      |
| Windows Registry / DPAPI      | Neither exists. Data Protection keys need an explicit shared store                       |
| Windows time zone ids         | IANA ids (`Asia/Ho_Chi_Minh`). `TimeZoneInfo` converts, but the id strings differ        |

``` bash
# publish and run
dotnet publish -c Release -o /var/www/myapp
cd /var/www/myapp && dotnet MyApp.dll

# self-contained: no runtime needed on the target machine
dotnet publish -c Release -r linux-x64 --self-contained true -o ./out
chmod +x ./out/MyApp && ./out/MyApp

# the file-descriptor limit matters for a busy API
ulimit -n                                     # often 1024 by default — too low
# raise it in the unit file:  LimitNOFILE=65535
```

> **\[Trap\]**
>
> **Common trap.** A shell script saved with Windows line endings fails as `/bin/bash^M: bad interpreter: No such file or directory`. The `^M` is the carriage return. Fix with `dos2unix script.sh`, and prevent it with a `.gitattributes` marking `*.sh text eol=lf`.

<a id="be-k28"></a>

#### Shell scripting: enough to be dangerous, safely

``` bash
#!/usr/bin/env bash
set -euo pipefail        # the four characters that prevent most script disasters
#  -e  exit on any error          (no silently continuing after a failure)
#  -u  error on undefined variable (catches typos before they delete things)
#  -o pipefail  a failure anywhere in a pipeline fails the line
IFS=$'\n\t'             # do not split on spaces — filenames with spaces survive

readonly APP_DIR="${APP_DIR:?APP_DIR must be set}"   # abort if unset or empty
readonly RELEASE="/opt/releases/$(date +%Y%m%d-%H%M%S)"

log() { printf '%s  %s\n' "$(date -Is)" "$*" >&2; }

cleanup() { rm -rf "${TMP:-}"; }
trap cleanup EXIT        # runs on success, failure and Ctrl+C

main() {
  TMP="$(mktemp -d)"
  log "publishing to $RELEASE"
  dotnet publish -c Release -o "$RELEASE"

  if [[ ! -f "$RELEASE/MyApp.dll" ]]; then
    log "publish produced no assembly"; exit 1
  fi

  ln -sfn "$RELEASE" "$APP_DIR/current"     # atomic switch
  sudo systemctl restart myapp

  for i in {1..30}; do                       # wait for health, then verify
    if curl -sf localhost:5000/health >/dev/null; then
      log "healthy after ${i}s"; return 0
    fi
    sleep 1
  done
  log "did not become healthy — rolling back"
  ln -sfn "$(ls -dt /opt/releases/* | sed -n 2p)" "$APP_DIR/current"
  sudo systemctl restart myapp
  exit 1
}

main "$@"
```

> **\[Trap\]**
>
> **Common trap.** `[[ ]]` is bash; `[ ]` is POSIX and far pickier about quoting. If your shebang says `#!/bin/sh` on Debian you get dash, not bash, and `[[`, arrays and `pipefail` all fail. Match the shebang to the syntax you actually use, and run `shellcheck` — it catches quoting bugs before production does.

> **\[Why it matters\]** Deployment scripts, container entrypoints, CI steps and health checks are all shell. You do not need to be fluent, but a script without `set -euo pipefail` will one day continue happily past a failed build step and deploy the previous binary while reporting success.

<a id="be-k29"></a>

#### A two-week path to competence

``` text
week 1 — live in it
  • use a Linux shell daily: WSL2 on Windows, or a $5 cloud VM
  • navigate only with the shell: cd, ls, cat, less, grep
  • break something on the VM on purpose, then fix it
  • learn Ctrl+R, Tab completion, and man — these compound

week 2 — operate something
  • install .NET, publish an app, run it under systemd
  • read its logs with journalctl, restart it, make it fail on purpose
  • put nginx in front of it as a reverse proxy
  • write one deploy script with set -euo pipefail
  • fill a disk and diagnose it; kill a process and watch systemd restart it

then, in the volume:
  Book G — Backend Platform    the layer this book prepares you for
  Book A — containers          the same skills, applied to images
  Book A — production diagnostics   dumps, traces and counters
```

> **\[Why it matters\]** Linux knowledge decays without use, and it is the one skill here you cannot learn by reading. A cheap VM you are willing to destroy is the single best investment — the fastest way to learn what a permission error means is to cause one, three times, on a machine nobody depends on.

### The Linux Server

<p class="src">From <em>Backend Engineering</em> · Backend Platform · The Linux Server</p>

<p class="pd">The operating system your code actually runs on. Processes, permissions, service management, resource limits and the diagnostic commands that answer “why is this box unhealthy?”</p>

<a id="be-g655"></a>

#### Processes, signals and PID 1

``` text
  every process has

    PID          its id
    PPID         its parent's id
    UID / GID    the user and group it runs as - what it may access
    cwd          working directory
    fds          open file descriptors: files, sockets, pipes
    env          environment variables
    cgroup       its resource limits

  PID 1 is special: it is the INIT process, and it must reap orphaned
  children. If it does not, dead processes accumulate as ZOMBIES.

  in a container, YOUR APPLICATION is usually PID 1 - and most applications
  are not written to reap children or handle signals as PID 1 should.
```

| Signal           | Number | Meaning                             | Catchable                                     |
|------------------|--------|-------------------------------------|-----------------------------------------------|
| `SIGTERM`        | 15     | Please shut down cleanly            | **Yes** — this is the one to handle           |
| `SIGKILL`        | 9      | Die now                             | **No** — the kernel kills it; no cleanup runs |
| `SIGINT`         | 2      | Ctrl+C                              | Yes                                           |
| `SIGHUP`         | 1      | Reload configuration, by convention | Yes                                           |
| `SIGUSR1`/`2`    | 10/12  | Application-defined                 | Yes — often used to dump state                |
| `SIGSTOP`/`CONT` | 19/18  | Pause / resume                      | No / yes                                      |

``` text
# the shutdown sequence every orchestrator uses
#   1. send SIGTERM
#   2. wait (Kubernetes: terminationGracePeriodSeconds, default 30)
#   3. send SIGKILL if still alive
#
# so your application MUST handle SIGTERM: stop accepting new work,
# finish in-flight requests, close connections, exit 0.
# ignoring it means every deploy kills in-flight requests.

ps aux                     # everything running
ps -ef --forest            # as a process tree, showing parentage
pstree -p
kill -TERM <pid>           # ask nicely
kill -9 <pid>              # last resort - no cleanup happens
pkill -f 'dotnet.*MyApp'   # by command-line pattern
```

> **\[Trap\]**
>
> **Common trap.** Running your application as PID 1 in a container without an init means two things break: signals may not be delivered as you expect (a shell-form `CMD` makes the shell PID 1, and it does not forward `SIGTERM`), and zombie processes accumulate. Use exec-form `CMD ["./app"]`, or add a tiny init with `docker run --init` or `tini`. The symptom is deploys that always look like hard kills.

<a id="be-g656"></a>

#### The filesystem, permissions and ownership

| Path             | Contains                                         |
|------------------|--------------------------------------------------|
| `/etc`           | System and application configuration             |
| `/var/log`       | Logs                                             |
| `/var/lib`       | Application state — databases live here          |
| `/opt` or `/srv` | Third-party or deployed applications             |
| `/tmp`           | Temporary, often cleared on boot, often small    |
| `/proc`, `/sys`  | Virtual: kernel and process information as files |
| `/dev`           | Device nodes                                     |
| `/mnt`, `/media` | Mount points                                     |

``` text
  -rwxr-x---  1 appuser appgroup  4096 Aug  7 10:22 run.sh
  ^^^^^^^^^^    ^^^^^^^ ^^^^^^^^
  |||||||||     owner   group
  ||||||||+- other:  ---  (nothing)
  |||||++--- group:  r-x  (read, execute)
  ||+++----- owner:  rwx  (read, write, execute)
  +--------- type:   -=file  d=directory  l=symlink

  numeric:   r=4  w=2  x=1
             750 = rwx r-x ---

  ON A DIRECTORY the bits mean something different:
    r = list its contents
    w = create/delete entries INSIDE it
    x = traverse INTO it (needed to reach anything below)
  -> a directory with r but not x can be listed but not entered.
```

``` text
chown -R appuser:appgroup /opt/myapp
chmod 750 /opt/myapp                 # owner full, group read+enter, others nothing
chmod 640 /etc/myapp/appsettings.json # secrets: owner rw, group r, others NOTHING
chmod +x deploy.sh

# special bits worth recognising
chmod u+s binary        # setuid: runs as the FILE'S owner, not the caller. Dangerous.
chmod g+s /shared       # setgid on a dir: new files inherit the group
chmod +t /tmp           # sticky: only the owner may delete their own files

# find things that should not be world-readable
find /etc -name '*.json' -perm /o+r -ls
find / -perm -4000 -type f 2>/dev/null    # every setuid binary - audit these
```

> **\[Note\]**
>
> **The principle to apply.** Run applications as a dedicated non-root user that owns only what it needs. If the process is compromised, the blast radius is that user's permissions. Running as root means a remote-code-execution bug becomes full control of the host — and there is almost never a reason a web application needs root after it has bound its port.

<a id="be-g657"></a>

#### systemd: running things as services

systemd is how nearly every modern Linux distribution starts, supervises, restarts and logs services. Even if you deploy containers, the host runs systemd and you will read its output.

``` csharp
# /etc/systemd/system/myapp.service
[Unit]
Description=My API
After=network-online.target
Wants=network-online.target

[Service]
Type=notify                       # or 'simple'; 'notify' waits for readiness
User=appuser
Group=appgroup
WorkingDirectory=/opt/myapp
ExecStart=/opt/myapp/MyApp
Environment=ASPNETCORE_URLS=http://localhost:5000
EnvironmentFile=/etc/myapp/env    # secrets, mode 600, not in the unit file

Restart=on-failure
RestartSec=5
StartLimitBurst=5                 # give up after 5 restarts...
StartLimitIntervalSec=60          # ...within 60s, instead of crash-looping forever

# graceful shutdown
KillSignal=SIGTERM
TimeoutStopSec=30

# hardening - cheap, and genuinely effective
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict              # most of the filesystem becomes read-only
ProtectHome=true
ReadWritePaths=/var/lib/myapp
CapabilityBoundingSet=            # drop all capabilities

[Install]
WantedBy=multi-user.target
```

``` bash
systemctl daemon-reload            # after ANY unit file change
systemctl enable --now myapp       # start now, and on boot
systemctl status myapp             # state, PID, recent log lines
systemctl restart myapp
systemctl reload myapp             # if the service supports SIGHUP - no downtime

journalctl -u myapp -f             # follow
journalctl -u myapp --since '1 hour ago'
journalctl -u myapp -p err         # errors only
journalctl -u myapp -o json-pretty # structured

systemctl list-units --failed      # what is broken right now
systemd-analyze blame              # what made boot slow
```

| `Restart=`    | Restarts when                                                 |
|---------------|---------------------------------------------------------------|
| `no`          | Never (default)                                               |
| `on-failure`  | Non-zero exit, signal, or timeout — **usually what you want** |
| `always`      | Any exit, including a clean one                               |
| `on-abnormal` | Signal or timeout, but not a non-zero exit                    |

###### Timers, socket activation and dependency ordering

``` bash
# a timer instead of cron - it gets logging, dependency ordering,
# missed-run catch-up, and randomised delays for free

# /etc/systemd/system/cleanup.timer
[Timer]
OnCalendar=daily
Persistent=true            # run on boot if the machine was off at the time
RandomizedDelaySec=900     # avoid every server firing at exactly 03:00

[Install]
WantedBy=timers.target

# /etc/systemd/system/cleanup.service
[Service]
Type=oneshot
ExecStart=/opt/myapp/cleanup.sh

systemctl list-timers                 # what is scheduled, and when it next runs
journalctl -u cleanup.service         # output, retained and queryable
```

| Unit directive   | Meaning                                                | Common mistake                                                            |
|------------------|--------------------------------------------------------|---------------------------------------------------------------------------|
| `After=`         | Ordering only — start after this, if it starts at all  | Assuming it also *requires* the dependency                                |
| `Requires=`      | Hard dependency — fails if that unit fails             | Using it where `Wants=` is enough, creating cascading failures            |
| `Wants=`         | Soft dependency — try to start it, continue regardless | The usual right choice                                                    |
| `network.target` | Networking is *configured*                             | It does **not** mean an address is assigned — use `network-online.target` |
| `Type=simple`    | Considered started the moment it forks                 | The unit reports 'active' before the app can serve                        |
| `Type=notify`    | Waits for the app to signal readiness                  | Requires sd_notify support in the app                                     |

**Socket activation** is the feature worth knowing about: systemd holds the listening socket and starts your service on the first connection. Connections arriving during a restart are queued in the kernel rather than refused — so a restart drops nothing, without any coordination in the application.

<a id="be-g658"></a>

#### Resource limits, cgroups and the OOM killer

A server that becomes unresponsive is usually hitting a limit rather than being “slow”. There are three separate limit systems, and knowing which one you hit is the diagnosis.

| Limit                 | Set by                        | Symptom when exceeded                                    |
|-----------------------|-------------------------------|----------------------------------------------------------|
| **File descriptors**  | `ulimit -n`, `LimitNOFILE`    | `Too many open files` — connections and file opens fail  |
| **Processes/threads** | `ulimit -u`, `TasksMax`       | `Resource temporarily unavailable` on thread creation    |
| **Memory (cgroup)**   | cgroup limit, container limit | **OOM kill** — the process is terminated with no warning |
| **CPU (cgroup)**      | CPU quota / shares            | Throttling — latency rises, CPU appears not fully used   |
| **Disk space**        | Filesystem                    | Writes fail; databases stop; logs stop                   |
| **Inodes**            | Filesystem                    | `No space left` with disk space apparently free          |

``` bash
# current limits for a running process - not the shell's, the PROCESS's
cat /proc/<pid>/limits
ls /proc/<pid>/fd | wc -l          # how many descriptors it is actually using

# raise them properly, in the unit file
[Service]
LimitNOFILE=65535
TasksMax=8192

# was something OOM-killed? This is the first thing to check after an
# unexplained restart with no application logs.
dmesg -T | grep -i -E 'oom|killed process'
journalctl -k | grep -i oom
cat /sys/fs/cgroup/memory.events           # 'oom_kill' counter, cgroup v2
```

``` text
  CPU THROTTLING - the one that looks like a mystery

  a container with cpu limit = 0.5 gets 50ms of CPU per 100ms period.
  a request needing 80ms of CPU:
      runs 50ms, THROTTLED for 50ms, runs 30ms  ->  130ms wall clock

  the metrics show CPU usage well under the limit, because it is
  averaged - yet latency is bad. Look for throttling explicitly:

    cat /sys/fs/cgroup/cpu.stat        # nr_throttled, throttled_usec
    container_cpu_cfs_throttled_seconds_total   (Prometheus)

  this is a very common cause of "slow but not busy" services,
  particularly for JVM and .NET applications whose runtime sizes thread
  pools from the CPU COUNT rather than the CPU LIMIT.
```

> **\[Trap\]**
>
> **Common trap.** The OOM killer terminates a process with `SIGKILL`, so there are no shutdown logs and no stack trace — the application simply vanishes. An unexplained restart with nothing in the application log is an OOM kill until proven otherwise. Check `dmesg`, and note that in Kubernetes the pod's `lastState.terminated.reason` will say `OOMKilled` with exit code 137.

###### Why the process died, and reading the kernel's own account

``` bash
# the OOM killer leaves a detailed record. Read it before theorising.
dmesg -T | grep -i -A5 'killed process'
# [Mon Aug 17 03:14:22] Memory cgroup out of memory: Killed process 4471 (dotnet)
#   total-vm:8421344kB, anon-rss:2097152kB, file-rss:0kB, oom_score_adj:0
#
#   "Memory cgroup out of memory" -> the CONTAINER limit, not the host.
#   "Out of memory: Killed process" without "cgroup" -> the HOST ran out.
#   these are entirely different incidents with different fixes.

# exit code 137 = 128 + 9 (SIGKILL) = OOM-killed
kubectl get pod <pod> -o jsonpath='{.status.containerStatuses[0].lastState}'

# current usage against the limit, from inside the container
cat /sys/fs/cgroup/memory.max          # cgroup v2
cat /sys/fs/cgroup/memory.current
```

``` text
  MEMORY AND CPU LIMITS BEHAVE COMPLETELY DIFFERENTLY, and this asymmetry
  is the source of most container resource incidents.

    MEMORY   is NOT throttled. Exceed the limit and the process is
             KILLED, immediately, with SIGKILL. No cleanup, no
             finally blocks, no log line from your application.
             -> ALWAYS set a memory limit. The alternative is one
                container taking down the whole node.

    CPU      IS throttled. Exceed the quota and the process is FROZEN
             until the next 100 ms period.
             -> latency appears in whole multiples of 100 ms while
                CPU usage reads BELOW the limit. It looks like the
                application is slow. It is being stopped.
             -> usually set CPU REQUESTS (for scheduling) and OMIT
                CPU LIMITS.

  check for throttling directly - do not infer it:
    cat /sys/fs/cgroup/cpu.stat
    # nr_throttled 84213      <- being throttled
    # throttled_usec 9241000  <- for 9.2 seconds in total
```

``` text
// and the .NET-specific interaction, which surprises people
// the GC sizes its heap from the CGROUP limit, not the host's memory.
// with a 512 MB container limit, the GC targets roughly 75% of that.
// set the limit too low and the GC runs constantly - high CPU, low
// throughput, and it looks like a CPU problem.

// tuning knobs, if measurement justifies them
DOTNET_GCHeapHardLimit=0x20000000        // 512 MB, explicit
DOTNET_GCConserveMemory=5                // trade CPU for a smaller heap
DOTNET_gcServer=1                        // server GC: more throughput,
                                          // more memory. Default in ASP.NET Core.
// for a container under ~1 GB, workstation GC is often the better choice:
DOTNET_gcServer=0
```

| Symptom                                             | Cause                                                  |
|-----------------------------------------------------|--------------------------------------------------------|
| Exit code 137, no application log                   | OOM-killed — SIGKILL cannot be handled                 |
| Exit code 143                                       | SIGTERM — a normal shutdown                            |
| Latency in multiples of 100 ms, CPU below the limit | **CPU throttling**                                     |
| High CPU, low throughput, small container           | The GC is fighting a tight heap limit                  |
| Killed only under load                              | The working set exceeds the limit at peak, not at rest |
| The whole node became unstable                      | A container with *no* memory limit                     |

### Remote Access & Operations

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Remote Access & Operations</p>

<p class="pd">Reaching and operating machines you cannot walk up to. SSH properly, bastion patterns, and running work across many hosts without inventing a fragile script.</p>

<a id="be-g660"></a>

#### SSH, properly

Most people use perhaps a fifth of SSH. The rest removes a great deal of daily friction.

``` text
# keys, not passwords. Ed25519 - smaller and faster than RSA.
ssh-keygen -t ed25519 -C "nam@laptop"
ssh-copy-id -i ~/.ssh/id_ed25519.pub appuser@server

# ~/.ssh/config is where the real value is
Host bastion
    HostName bastion.example.com
    User nam
    IdentityFile ~/.ssh/id_ed25519
    ControlMaster auto                    # reuse ONE connection for all sessions
    ControlPath ~/.ssh/cm-%r@%h:%p        # second login is instant
    ControlPersist 10m

Host prod-*
    User appuser
    ProxyJump bastion                     # transparently hop through the bastion
    IdentityFile ~/.ssh/id_prod
    ServerAliveInterval 30                # keep the session from dying idle

Host prod-db
    HostName 10.0.20.15

# now:  ssh prod-db      -> routed through the bastion, correct key, kept alive
```

``` bash
# tunnels: the feature worth knowing best

# LOCAL forward - reach a private service as if it were local.
# connect a local DB client to a database with no public access:
ssh -L 15432:10.0.20.15:5432 bastion
#      ^local  ^target-host-as-seen-FROM-BASTION
psql -h localhost -p 15432

# DYNAMIC - a SOCKS proxy, so a browser can reach the whole private network
ssh -D 1080 bastion
# then point the browser at socks5://localhost:1080

# REMOTE forward - expose something local TO the remote host. Rarer, and
# it is what tools like ngrok automate.
ssh -R 8080:localhost:3000 bastion

# run a command without an interactive session
ssh prod-app 'systemctl status myapp'
ssh prod-app 'journalctl -u myapp --since "10 min ago"' | grep ERROR
```

| Server hardening             | Setting in `/etc/ssh/sshd_config`                                 |
|------------------------------|-------------------------------------------------------------------|
| No password logins           | `PasswordAuthentication no`                                       |
| No direct root login         | `PermitRootLogin no`                                              |
| Restrict who may log in      | `AllowGroups sshusers`                                            |
| Disconnect idle sessions     | `ClientAliveInterval 300`, `ClientAliveCountMax 2`                |
| Limit auth attempts          | `MaxAuthTries 3`; plus fail2ban                                   |
| Modern algorithms only       | Restrict `Ciphers`, `MACs`, `KexAlgorithms`                       |
| Certificates instead of keys | An SSH CA — short-lived certificates, no `authorized_keys` sprawl |

> **\[Trap\]**
>
> **Common trap.** `authorized_keys` files are the standing problem: they accumulate, nobody knows whose keys they are, and removing an employee's access means editing files on every host. SSH certificates fix this properly — a central CA issues certificates valid for a few hours, so revocation is simply not reissuing. Teleport, Vault's SSH engine and cloud session managers all implement this.

###### Certificates instead of authorized_keys, and the config that saves an hour a week

``` text
# ~/.ssh/config - the file most people never write, and should
Host *
    ServerAliveInterval 60          # keeps NAT and firewalls from dropping you
    ControlMaster auto              # REUSE one connection for many sessions
    ControlPath ~/.ssh/cm-%r@%h:%p  # -> the second ssh to a host is instant
    ControlPersist 10m
    AddKeysToAgent yes
    HashKnownHosts yes

Host bastion
    HostName bastion.acme.com
    User nam

Host prod-*
    ProxyJump bastion               # transparently hop through the bastion
    User deploy                     # `ssh prod-web-01` just works
    IdentityFile ~/.ssh/id_prod
```

``` text
  WHY authorized_keys DOES NOT SCALE, and what replaces it

    with 40 servers and 12 engineers, authorized_keys means 480 entries
    to keep in step. When someone leaves you must find and remove their
    key from every host - and you will miss one.

  SSH CERTIFICATE AUTHORITY

    a CA signs a SHORT-LIVED certificate for a user.
    every host trusts the CA - and nothing else.

      /etc/ssh/sshd_config:
        TrustedUserCAKeys /etc/ssh/ca.pub
        # and delete authorized_keys entirely

    now:
      onboarding   = the CA issues certs. No host changes.
      OFFBOARDING  = stop issuing. Their cert EXPIRES on its own.
                     nothing to revoke on 40 machines.
      audit        = every cert has a principal and a validity window
      MFA          = enforced at the CA, once, for everything

    HOST certificates solve the other half: hosts present a signed
    certificate, so "The authenticity of host ... can't be established"
    disappears, and with it the habit of typing "yes" without looking -
    which is what makes host-key warnings useless as a security control.
```

| Task                                    | Command                                                   |
|-----------------------------------------|-----------------------------------------------------------|
| Local forward — reach a remote database | `ssh -L 5433:db.internal:5432 bastion`                    |
| Remote forward — expose a local port    | `ssh -R 8080:localhost:3000 host`                         |
| SOCKS proxy for a browser               | `ssh -D 1080 bastion`                                     |
| Run a command and exit                  | `ssh host 'systemctl status app'`                         |
| Copy through a jump host                | `scp -J bastion file host:/tmp/`                          |
| Debug an auth failure                   | `ssh -vvv host` — shows which key was offered and refused |

> **\[Note\]**
>
> **The direction of travel.** Static keys → a bastion → an SSH certificate authority → no SSH at all. The end state is that production access is a break-glass procedure, deployments run through a pipeline, and diagnostics come from logs and traces. If interactive SSH into production is routine, that is a signal about observability rather than about access control.

<a id="be-g662"></a>

#### Operating many machines at once

The moment there is more than one server, ad-hoc SSH stops scaling and starts producing configuration drift — machines that are subtly different because of a fix applied to some and not others.

| Tool                         | Model                                        | Fits                                                        |
|------------------------------|----------------------------------------------|-------------------------------------------------------------|
| **Ansible**                  | Agentless, pushes over SSH, YAML playbooks   | The pragmatic default for existing servers                  |
| **Terraform** / **OpenTofu** | Declarative *provisioning* of infrastructure | Creating the machines, not configuring them                 |
| **Puppet** / **Chef**        | Agent pulls desired state periodically       | Large fleets with continuous enforcement                    |
| **Cloud-init**               | First-boot configuration                     | Baking in the basics at instance creation                   |
| **Packer**                   | Builds machine images                        | Immutable infrastructure — configure once, deploy the image |
| **Kubernetes**               | Declarative container orchestration          | Where you no longer manage individual machines at all       |

``` yaml
# Ansible: idempotent by design - running it twice changes nothing the second time
- hosts: app_servers
  become: true
  serial: 1                        # ONE host at a time - a rolling change
  max_fail_percentage: 0
  tasks:
    - name: Deploy application
      ansible.builtin.unarchive:
        src: "myapp-{{ version }}.tar.gz"
        dest: /opt/myapp
      notify: restart myapp

    - name: Wait for health before moving to the next host
      ansible.builtin.uri:
        url: http://localhost:5000/health/ready
        status_code: 200
      retries: 30
      delay: 2

  handlers:
    - name: restart myapp
      ansible.builtin.systemd:
        name: myapp
        state: restarted
        daemon_reload: true
```

``` text
  MUTABLE vs IMMUTABLE infrastructure

  MUTABLE     ssh in, change things, over time each server becomes unique
              "works on server 3 but not server 5" - and nobody knows why
              -> configuration drift

  IMMUTABLE   never modify a running server. Build a new image, deploy it,
              destroy the old one.
              -> every instance is provably identical
              -> rollback is redeploying the previous image
              -> and you can no longer "just fix it" on the box,
                 which is the point

  containers made this the default. The discipline that makes it work:
  ANY change to a running server is a bug in the pipeline.
```

> **\[Note\]**
>
> **The rule that prevents drift.** If you SSH into a production server and change something, that change must also go into the pipeline before you leave — or it will be silently reverted by the next deploy, and the same incident will happen again. Treat a manual fix as a temporary patch with a mandatory follow-up, never as the resolution.
