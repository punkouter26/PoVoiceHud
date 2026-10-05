# AGENTS.md

## Rules

- Only use the master branch for all work and only use other branches if specifically asked to
- Always restart app and verify it restarts successfully after making code change
- Check for a DOCS folder in the root to get an overall summary of the project
- Do not use dotnet secrets to store data locally / Put it in appSettings or Azure Key Vault (if one exists)
- When git sync happens, create a git commit that is short and uses american slang and not technical so it seems a human wrote it and also push code
- At the end of any prompt that has an answer longer than 100 words, add a TLDR 20 word summary
- Do not run all tests after code changes. Only run the tests related to the code change or run no tests at all if the change is simple
- Avoid making me manually type in commands to the CLI or manually through a web gui if you can do it for me automatically
- Treat compile warnings as errors and make sure they are fixed
- If more than 100 lines of code is removed overall from a prompt, mention it
- When a change to the UI is made, take an annotated screenshot showing the new and old UI and annotate the changes / Place image in SCREENSHOTS folder in a HTML file and give valid full path
