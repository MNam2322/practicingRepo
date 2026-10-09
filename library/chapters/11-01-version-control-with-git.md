---
id: "11-1"
number: "11.1"
title: "Version control with Git"
part: 11
part_title: "Shipping software"
words: 4427
minutes: 19
---

# 11.1 Version control with Git

> **Quick look.** Git's small underlying model, and the everyday commands that follow from it.
>
> - Git stores content-addressed blobs, trees and commits that form a graph.
> - Branches and tags are labels on commits; HEAD is what you have checked out.
> - Commit small, well-described snapshots; merge through reviewed pull requests.
> - Rebase replays commits as new ones: fine for your own work, never for shared history.
> - Undo with restore and reset locally, revert for shared history; the reflog is your safety net.
> - Find changes with log, blame and bisect; never commit secrets or build output; integrate small changes often.


### The big picture

Every change to a codebase needs to be recorded: who changed what, when and why, so that changes can be reviewed, combined with other people's, released, and undone when they turn out to be wrong. Version control does that, and almost everywhere today it means Git. Git was written by Linus Torvalds in 2005 to manage the Linux kernel. It now underlies GitHub, GitLab and Azure DevOps, and nearly every build pipeline starts by fetching code from a Git repository.

Git has a reputation for being confusing, mostly because its commands are learned as recipes without the model underneath. The model is small. Git stores snapshots of your files, named by the hash of their content, from Secrets and cryptography basics (10.5), and links them into a graph, from Graphs (3.6). Branches are just movable labels on points in that graph. This chapter builds that model first. It then covers everyday work, merging and conflicts, rebasing and when not to, undoing mistakes safely, finding the commit that broke something, and the team workflow most organizations now use. The example is a script that runs a complete Git session. It fixes the author and dates, so every commit id it prints is identical on your machine and this chapter shows the real ones.

### Before you start

**Builds on:** 10.5 Secrets and cryptography basics, 3.6 Graphs, 3.7 Searching and sorting, 1.3 Text is numbers too.

**Ideas explained later:**

- **Azure** (11.5): Microsoft's cloud platform, common in organizations built on Microsoft tools.
- **Single point of failure** (12.5): A component whose failure alone brings the system down.
- **Code review** (13.2): Another person reading a change before it is merged, to check its correctness, design and safety.
- **Docker** (11.3): The tool that popularized containers, used to build images and run containers.

**Tools and technologies named here:**

- **CI** (11.4): Continuous integration: automatically building and testing every change as soon as it is pushed.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is a branch in Git, underneath?**

> A small file holding the id of one commit: a movable label. Committing on a branch moves its label to the new commit. Creating a branch copies nothing.

**2. What is the staging area?**

> The set of changes that will go into the next commit. `git add` puts changes there, so a commit can contain some of your edits and not others.

**3. Why shouldn't you rebase commits that others have already pulled?**

> Rebasing creates new commits with new ids to replace the old ones. Anyone who built on the old commits now has a history that diverges from yours, and merging them back duplicates or conflicts.

**4. What is the difference between git revert and git reset?**

> `revert` adds a new commit that undoes an earlier one, so history is kept and it is safe on shared branches. `reset` moves the branch label back, which effectively removes commits from that branch, so it is for local work only.

**5. You deleted a branch with unmerged commits. Are they gone?**

> Usually not. The reflog records where your branches and HEAD have pointed recently, typically for 90 days, so you can find the commit id and create a branch on it again.

**6. What does git bisect do?**

> It binary-searches the history between a known good and a known bad commit, checking out the midpoint each time, to find the first commit that introduced a problem. `git bisect run` automates it with a test script.

**7. What is trunk-based development?**

> Everyone integrates small changes into one main branch frequently, at least daily, through short-lived branches and pull requests, with unfinished features hidden behind flags rather than kept on long-lived branches.

### Why version control

**Version control** records the history of a set of files, so that every past state can be recovered, changes can be compared and explained, and several people can work on the same files at once. Older systems such as Subversion and Team Foundation Version Control were centralized: one server held the history, and each developer had only a working copy. **Git** is distributed. Every clone is a complete **Git repository** with the full history, so most operations are local and fast, work offline, and there is no single point of failure. A hosting service such as GitHub, GitLab or Azure DevOps holds the shared copy that everyone synchronizes with, and adds code review, permissions and pipelines around it.

### How Git stores things

Git's model has three layers, and knowing them makes every command make sense.

At the bottom is a store of objects, each named by the hash of its content. A blob holds the content of one file. A tree holds a folder listing: names, and the ids of blobs and other trees. A **Git commit** holds the id of one tree, a complete snapshot of the project, plus the ids of its parent commits, the author, a date and a message. Because ids are hashes of content, identical files are stored once, any corruption is detectable, and a commit id fixes the entire history behind it. Change anything in an old commit and every id after it changes. The example shows that a file's id is just the SHA-1 hash of a short header and its content. Git is gradually moving to SHA-256, since SHA-1 is broken, as Secrets and cryptography basics (10.5) noted.

Because each commit points to its parents, history is a directed acyclic graph, from Graphs (3.6). A merge commit has two parents.

At the top are references: human-readable names for commit ids. A **Git branch** is a reference that moves: committing on it makes it point to the new commit. A tag is a reference that stays put, typically naming a release such as `v2.4.0`. **HEAD** is the reference to what you have checked out, usually a branch. Creating a branch writes one small file; switching branches updates your files to that commit's snapshot.

### Everyday work

Three places matter while you work. The working directory holds your files as you edit them. The **staging area**, also called the index, holds what will go into the next commit. The repository holds the commits. `git status` shows what differs between them. `git add` copies changes into the staging area, so one commit can contain some of your edits and not others. `git commit` records the staged snapshot. `git diff` shows unstaged changes, `git diff --staged` shows what you are about to commit, and `git log` shows history.

Good commits are small and do one thing. A good message says what changed in its first line, in the imperative, "Add gate to departures", and why in the body when that isn't obvious. Six months later, "why" is what people need, and `git log` and `git blame` are where they will look.

### Branches and merging

Work happens on branches. You create a branch for a change, commit to it, and later bring it back into the main branch by merging.

If the main branch hasn't moved since the branch was created, the merge is a **fast-forward merge**: the main label simply moves forward to the branch's last commit, with no new commit. If both have moved on, Git makes a **merge commit** with two parents. To combine the changes, it finds the merge base, the most recent common ancestor, and applies both sides' changes relative to it. That is called a three-way merge.

When both sides changed the same lines, Git can't choose, and stops with a **merge conflict**. It writes both versions into the file between conflict markers: `<<<<<<<` your side, `=======`, their side, `>>>>>>>`. You edit the file to the correct result, which is often a combination of both, stage it and commit. The example produces exactly this: one branch delays a flight, another adds its gate, on the same line. Conflicts are normal, not an error; they get rarer and smaller when branches are short-lived.

### Rebasing

**Rebase** is the alternative to merging. It takes the commits on your branch and replays them one by one on top of another commit, typically the latest main, producing a straight line of history with no merge commit. It is how you bring a branch up to date before review. Interactive rebase, `git rebase -i`, lets you reorder, combine and reword your local commits, to tidy them before others see them.

Rebasing creates new commits with new ids; the originals are left behind. That leads to the one firm rule: never rebase commits that other people already have. If you do, their copies and yours diverge, and the next merge duplicates work or conflicts. Rebase your own local or unshared work freely; merge shared history. If you must overwrite a branch you pushed, use `git push --force-with-lease`, which refuses if someone else pushed in the meantime, rather than plain `--force`.

### Working with others

A **Git remote** is a named link to another copy of the repository, usually `origin` for the hosted copy. `git clone` copies a repository and sets up `origin`. `git fetch` downloads new commits and updates remote-tracking branches such as `origin/main`, without touching your work. `git pull` is a fetch followed by a merge, or a rebase if configured. `git push` uploads your commits and moves the remote's branch.

Changes reach the main branch through a **pull request**, called a merge request in GitLab. It proposes merging a branch, shows the diff for review, runs the pipeline's checks, from CI/CD pipelines (11.4), and records the discussion. Branch protection rules on the main branch can require review, passing checks and an up-to-date branch before merging. Hosts offer three ways to complete a pull request: a merge commit, a squash merge that combines the branch into a single commit, or a rebase.

### Undoing things

Git gives you several ways back, and the right one depends on whether others have the commits.

For uncommitted changes, `git restore file` throws away edits to a file, and `git stash` sets all current changes aside to reapply later.

For commits that are only local, `git reset` moves the branch label back. `--soft` keeps the changes staged, the default `--mixed` keeps them in the working directory, and `--hard` discards them. `git commit --amend` replaces the last commit, to fix its message or add a forgotten file.

For commits that others have, use **revert**. `git revert` creates a new commit that undoes an earlier one. History stays intact and nobody's copy breaks, which makes it the safe way to back out a bad change on a shared branch, including a bad release.

Behind all of this is the **reflog**, Git's record of where HEAD and each branch have pointed, kept locally for about 90 days by default. Commits you reset away, rebased over or left on a deleted branch are still in the object store. The reflog tells you their ids, so you can put a branch back on them, as the example does. It is very hard to lose committed work in Git.

### Finding things

`git log -S "text"` finds commits that added or removed a string. `git log -- path` shows the history of one file. `git blame file` shows, for each line, the commit that last changed it, which leads you to the pull request and its discussion.

When something broke and you don't know when, **bisect** finds the commit by binary search, from Searching and sorting (3.7). You mark one commit good and one bad; Git checks out the midpoint; you test and mark it; and it halves the range each time. A thousand commits need about ten tests. `git bisect run` automates the whole search with any script that exits 0 for good and non-zero for bad, such as a failing unit test.

### Seeing it for real

This script builds a throwaway repository and walks through the ideas in this chapter. It fixes the author and dates, so the ids it prints are the same on every machine, and yours should match the output below.

```bash
#!/bin/sh
# git-tour.sh: run with: sh git-tour.sh   (needs only git; builds a throwaway repository in ./tour)
set -e
rm -rf tour && git init -q -b main tour && cd tour
export GIT_AUTHOR_NAME="Ada" GIT_AUTHOR_EMAIL="ada@example.com" GIT_COMMITTER_NAME="Ada" GIT_COMMITTER_EMAIL="ada@example.com"
n=0
stamp() {                                    # fixed dates, so every commit id below is the same on any machine
  n=$((n + 1)); d=$(printf "2026-09-%02dT09:00:00Z" "$n"); export GIT_AUTHOR_DATE="$d" GIT_COMMITTER_DATE="$d"
}
commit() { stamp; git add -A && git commit -q -m "$1"; }

echo "== 1. Everything is stored under the hash of its content"
printf 'BA117 08:25\n' > departures.txt
git hash-object departures.txt                                  # Git's id for this file's content...
printf 'blob 12\0BA117 08:25\n' | sha1sum | cut -c1-40         # ...is SHA-1 of a short header plus the content

echo "== 2. Commits, and a branch that is just a movable pointer"
commit "Add departures"
printf 'BA117 08:25\nQF1 16:00\n' > departures.txt && commit "Add QF1"
git switch -q -c gates                                         # a new branch pointing at the same commit
printf 'BA117 08:25 gate B12\nQF1 16:00\n' > departures.txt && commit "Add gate for BA117"
git switch -q main
printf 'BA117 08:30\nQF1 16:00\n' > departures.txt && commit "Delay BA117 to 08:30"
git log --oneline --graph --all

echo "== 3. Both branches changed the same line: a merge conflict"
git merge gates || true
cat departures.txt
printf 'BA117 08:30 gate B12\nQF1 16:00\n' > departures.txt   # resolve: keep both changes
commit "Merge gates, keeping the new time and the gate"
git log --oneline --graph

echo "== 4. Rebase replays commits on a new base, making new commits"
git switch -q -c crew HEAD~1
printf 'Captain: Linus\n' > crew.txt && commit "Add crew list"
stamp && git rebase -q main && git log --oneline -3

echo "== 5. Revert undoes a commit with a new commit; history is kept"
git switch -q main && git merge -q --ff-only crew
stamp && git revert --no-edit HEAD >/dev/null && git log --oneline -3 && ls

echo "== 6. A deleted branch's commits are still in the reflog"
git switch -q -c experiment
printf 'draft\n' > notes.txt && commit "Draft seat-map notes"
git switch -q main && git branch -q -D experiment              # the branch is gone, and its commit was never merged
lost=$(git reflog --format='%h %gs' | grep -m1 "Draft seat-map notes" | cut -d' ' -f1)
git branch rescued "$lost" && git log --oneline -1 rescued     # the commit is still there: give it a branch again

echo "== 7. Bisect finds the commit that broke a test"
for i in 1 2 3 4 5 6; do
  if [ "$i" -ge 4 ]; then echo "QF1 1600" >> times.txt; else echo "QF1 16:00" >> times.txt; fi   # commit 4 breaks the format
  commit "Update times $i"
done
git bisect start HEAD HEAD~6 >/dev/null
git bisect run sh -c '! grep -q "QF1 1600" times.txt' | grep "is the first bad commit"
git bisect reset >/dev/null 2>&1
```

Its output:

```text
== 1. Everything is stored under the hash of its content
340083ea353f54a9fdc5d17656db2f7056449359
340083ea353f54a9fdc5d17656db2f7056449359
== 2. Commits, and a branch that is just a movable pointer
* 985695e Delay BA117 to 08:30
| * cd971ad Add gate for BA117
|/  
* b0eb523 Add QF1
* a239f97 Add departures
== 3. Both branches changed the same line: a merge conflict
Auto-merging departures.txt
CONFLICT (content): Merge conflict in departures.txt
Automatic merge failed; fix conflicts and then commit the result.
<<<<<<< HEAD
BA117 08:30
=======
BA117 08:25 gate B12
>>>>>>> gates
QF1 16:00
*   fbda6f8 Merge gates, keeping the new time and the gate
|\  
| * cd971ad Add gate for BA117
* | 985695e Delay BA117 to 08:30
|/  
* b0eb523 Add QF1
* a239f97 Add departures
== 4. Rebase replays commits on a new base, making new commits
0f66868 Add crew list
fbda6f8 Merge gates, keeping the new time and the gate
985695e Delay BA117 to 08:30
== 5. Revert undoes a commit with a new commit; history is kept
48c353b Revert "Add crew list"
0f66868 Add crew list
fbda6f8 Merge gates, keeping the new time and the gate
departures.txt
== 6. A deleted branch's commits are still in the reflog
5665935 Draft seat-map notes
== 7. Bisect finds the commit that broke a test
b6a6f2277f70db0b3db7f13647be3409e5376431 is the first bad commit
```

Notice what the ids show. The file id in step 1 equals the SHA-1 of its content with a header, computed by an ordinary hashing tool. The rebased crew commit in step 4 has a new id, because its parent changed. The revert in step 5 adds a commit rather than removing one, and `ls` shows `crew.txt` gone. The commit in step 6 was on a deleted, unmerged branch, yet the reflog still found it. And bisect in step 7 named the commit "Update times 4", exactly where the broken format started.

### What doesn't belong in Git

A **.gitignore** file lists patterns Git should ignore: build output such as `bin/` and `obj/`, dependency folders such as `node_modules`, editor settings and local configuration. Standard templates exist for every language.

Never commit secrets. As Secrets and cryptography basics (10.5) warned, a committed secret stays in the history even after the file changes; rotate it and use secret scanning. Large binary files, such as videos, datasets and installers, bloat every clone forever; keep them elsewhere, or use Git LFS, which stores them outside the repository. A `.gitattributes` file can normalize line endings, the CRLF and LF difference from Text is numbers too (1.3), so Windows and Linux developers don't produce whole-file diffs.

### How teams work

**Trunk-based development** is the workflow most high-performing teams use. Everyone integrates into one main branch, the trunk, at least daily, through short-lived branches and pull requests that live hours or a day or two. Incomplete features are merged but hidden behind feature flags, from CI/CD pipelines (11.4), rather than living for weeks on a branch. Small, frequent merges mean small conflicts, fast review and a main branch that is always releasable.

The older GitFlow model keeps long-lived develop, release and hotfix branches. It suits software shipped in versioned releases to customers who install it, and it adds overhead and large merges elsewhere. Whatever the model, protect the main branch, require review and passing checks, and keep branches short.

Organizations also choose between one repository for many projects, a monorepo, and one repository per project or service. A monorepo makes changes across projects atomic and sharing easy, and needs tooling to build only what changed. Many repositories keep boundaries firm and permissions simple, and make cross-cutting changes slow.

### In short

Git stores content-addressed objects: blobs for files, trees for folders and commits for snapshots with parents, forming a graph. Branches and tags are labels on commits, and HEAD is what you have checked out. Stage changes, then commit small, well-described snapshots. Merges fast-forward or create merge commits; conflicts mark both versions for you to resolve. Rebase replays commits as new ones: fine for your own work, never for shared history. Push and pull through remotes, and merge through reviewed pull requests. Undo with restore, reset for local work, and revert for shared history, with the reflog as a safety net. Find changes with log, blame and bisect. Ignore build output, never commit secrets, and integrate small changes into the trunk often.

### What goes wrong

**Rewriting shared history.** A rebase or force push on a shared branch leaves teammates with diverged histories. Rebase only unshared work; use `--force-with-lease` when you must.

**Long-lived branches.** A branch open for weeks meets huge conflicts and surprises at merge time. Merge small changes daily and hide unfinished work behind flags.

**Committed secrets.** A key committed once lives in history forever. Rotate it immediately and add secret scanning.

**Huge, mixed commits.** One commit that reformats files, fixes a bug and adds a feature is impossible to review or revert cleanly. Commit one change at a time.

**`reset --hard` on the wrong thing.** Uncommitted work is discarded, and it isn't in the reflog. Commit or stash before resetting.

**Resolving conflicts carelessly.** Taking "ours" or "theirs" wholesale silently drops the other side's change. Read both sides, and run the tests after resolving.

**Build output and binaries in the repository.** Every clone grows slower. Use `.gitignore` and keep large binaries elsewhere.

### Cost model

Most Git operations are local and take milliseconds: commits, branches, diffs, logs and switching between recent branches. Creating a branch costs one small file. A clone downloads the entire history once, so repositories with years of history or large binaries clone slowly; shallow and partial clones, `--depth 1` or `--filter=blob:none`, help pipelines. Merge and rebase costs grow with how far branches have drifted, in conflicts to resolve rather than in computer time. Bisect needs about log₂ of the number of commits in tests, so ten tests cover a thousand commits.

### In other languages (optional)

**Every stack.** Git is language-independent: the same commands serve C#, TypeScript and anything else. Only the `.gitignore` patterns differ.

**Tools.** Visual Studio, Rider and VS Code have Git built in, with visual diffs, merge editors and history. GitHub CLI and the Azure DevOps CLI script pull requests and reviews from the terminal.

**Other systems.** Mercurial is a distributed system similar to Git, and Perforce is still common where huge binary assets dominate, such as game development.

### Key terms

- **Version control**: Recording the history of files so past states can be recovered and changes combined and explained.
- **Git**: The distributed version control system used almost everywhere, created in 2005.
- **Git repository**: A project's files together with their complete Git history.
- **Git commit**: A snapshot of the whole project, with its parent commits, author, date and message, named by its hash.
- **Staging area**: The set of changes that will go into the next commit. Also called the index.
- **Git branch**: A movable label pointing at a commit, moved forward by each new commit on it.
- **HEAD**: Git's reference to what is currently checked out, usually a branch.
- **Fast-forward merge**: A merge that just moves a branch label forward, because only one side had new commits.
- **Merge commit**: A commit with two parents that combines two lines of history.
- **Merge conflict**: When both sides of a merge changed the same lines and Git can't choose between them.
- **Rebase**: Replaying a branch's commits on top of another commit, creating new commits.
- **Git remote**: A named link to another copy of the repository, usually called origin.
- **Pull request**: A proposal to merge a branch, with a diff for review, discussion and automated checks.
- **Revert**: A new commit that undoes an earlier commit, keeping history intact.
- **Reflog**: Git's local record of where HEAD and each branch have pointed, used to recover lost commits.
- **Bisect**: Finding the commit that introduced a problem by binary search through history.
- **.gitignore**: A file of patterns for files Git should not track, such as build output.
- **Trunk-based development**: Integrating small changes into one main branch frequently, with short-lived branches.

### Where this leads

Turning the code in a repository into something runnable, and managing its dependencies, is in Builds and dependencies (11.2). Packaging it as a container is in Containers and Docker (11.3). Running checks on every pull request and deploying from the main branch is in CI/CD pipelines (11.4).

### Check yourself

1. Run `git-tour.sh` and compare every id with the output above. Then change one date in the script and note which ids change and which don't.
2. In the tour's repository, run `git cat-file -p HEAD` and then `git cat-file -p` on the tree id it shows. Describe what each object contains.
3. Make a conflict of your own in the tour repository, abort it with `git merge --abort`, then redo it and resolve it with your editor's merge tool.
4. Use interactive rebase to combine the six "Update times" commits into one, and check that the ids of the earlier commits didn't change.
5. In a repository you work on, find which commit last changed an important line with `git blame`, and follow it to its pull request.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Finding the commit that broke checkout

Checkout worked in release 4.12 and fails in 4.19. There are 180 commits in between.

*Let git bisect search*

```bash
git bisect start
git bisect bad v4.19
git bisect good v4.12
git bisect run dotnet test tests/Checkout.Tests --filter Category=Smoke   # ~8 test runs instead of 180
git bisect reset
```

> **Lesson:** Binary search over history finds a regression in a handful of steps when you have a test that detects it.

#### Example 2: A secret committed by mistake

A developer pushed `appsettings.Development.json` containing a real payment API key, then deleted it in the next commit.

*What to do, in order*

```bash
# 1. Rotate the key at the provider NOW: it is in history and in every clone and fork
# 2. Then clean history if policy requires it
git filter-repo --path src/Api/appsettings.Development.json --invert-paths
# 3. Prevent the next one
dotnet user-secrets set "Payments:ApiKey" "<value>"     # local secrets live outside the repo
# and enable secret scanning / push protection on the repository
```

> **Lesson:** Deleting a committed secret doesn't remove it. Rotate first, then clean up and prevent repeats.
