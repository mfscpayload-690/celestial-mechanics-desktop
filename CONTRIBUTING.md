# Contributing to Celestial Mechanics

First off, thank you for considering contributing to Celestial Mechanics! It's people like you that make this simulation project such a great tool for learning and exploring dynamics.

## Where do I go from here?

If you've noticed a bug or have a feature request, make sure to check our [Issues](../../issues) to see if someone else has already created a ticket. If not, go ahead and create one!

## Fork & create a branch

If this is something you think you can fix, then fork Celestial Mechanics and create a branch with a descriptive name.

## Get the development environment running

Ensure you have the following installed:
* .NET 8 SDK
* C++ workload (if you are touching the engine module)

Build the project:
```bash
dotnet build
```

## Pull Request Process

1. Ensure any install or build dependencies are removed before the end of the layer when doing a build.
2. Update the README.md with details of changes to the interface, this includes new environment variables, exposed ports, useful file locations and container parameters.
3. Keep your PRs as small as possible. Provide a clear description of the problem you are solving and the solution you have proposed.
4. If your PR resolves an open issue, link to the issue in the description.

## Styleguides

* Use standard C# record struct semantics where appropriate for performance.
* Favor explicit variable types over `var` unless the right-hand side clearly indicates the type.
* Keep physics logic and rendering logic completely decoupled.
* Leave comments explaining *why* a particular mathematical model or optimization was used, rather than *what* the code is doing.

## Any questions?

If you still need help, feel free to open a discussion or ask for help in an issue.
