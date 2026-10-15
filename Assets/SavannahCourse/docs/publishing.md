# Publish the package

Run these commands from the root of the `studentstarter` checkout after reviewing
the staged files. They prepare the current package branch; they do not publish a
student's separate Unity project.

```text
git add SavannahCourse SavannahCourse.meta README.md
git status --short
git commit -m "Add static savannah course package"
git push -u origin feat/static-savannah-course
```

Create and review the resulting pull request before merging. Once published,
students should record the merged commit they import.
