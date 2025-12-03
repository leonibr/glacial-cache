# GlacialCache.PostgreSQL Documentation

This directory contains comprehensive documentation for GlacialCache.PostgreSQL, a high-performance distributed cache provider for .NET using PostgreSQL.

## Documentation Structure

### 📚 Core Documentation

1. **[Getting Started](getting-started.md)** - Quick start guide
   - Prerequisites and installation
   - Minimal working example with ASP.NET Core
   - Configuration basics
   - First cache operations
   - **Start here if you're new to GlacialCache**

2. **[Core Concepts](concepts.md)** - Understanding the fundamentals
   - Data model and PostgreSQL schema
   - Expiration behavior (absolute, sliding, combined)
   - Cleanup strategy and maintenance
   - Comparisons with IMemoryCache and Redis

3. **[Configuration](configuration.md)** - Deep dive into options
   - Complete reference for `GlacialCachePostgreSQLOptions`
   - Connection, cache, maintenance, resilience, infrastructure, security, and monitoring options
   - Production-ready configuration examples
   - Azure Managed Identity setup
   - Binding from `appsettings.json`

4. **[Architecture](architecture.md)** - System design and internals
   - Component overview
   - Request flow diagrams (Mermaid)
   - Background maintenance pipeline
   - Manager election mechanism
   - Connection management and pooling
   - Resilience patterns and logging

5. **[Troubleshooting](troubleshooting.md)** - Common issues and solutions
   - Connection and connectivity problems
   - Schema and table issues
   - Expiration and cleanup problems
   - Performance and locking issues
   - Azure Managed Identity troubleshooting
   - Logging and diagnostics

## Quick Navigation

### By Role

- **First-time users**: Start with [Getting Started](getting-started.md)
- **Developers integrating GlacialCache**: Read [Getting Started](getting-started.md) → [Concepts](concepts.md) → [Configuration](configuration.md)
- **DevOps/Platform engineers**: Focus on [Configuration](configuration.md) → [Architecture](architecture.md) → [Troubleshooting](troubleshooting.md)
- **Troubleshooting issues**: Jump to [Troubleshooting](troubleshooting.md)

### By Topic

| Topic | Document |
|-------|----------|
| Installation and setup | [Getting Started](getting-started.md) |
| How expiration works | [Concepts](concepts.md) |
| Connection pooling | [Configuration](configuration.md) + [Architecture](architecture.md) |
| Cleanup/maintenance | [Concepts](concepts.md) + [Architecture](architecture.md) |
| Multi-instance deployment | [Configuration](configuration.md) + [Troubleshooting](troubleshooting.md) |
| Azure Managed Identity | [Configuration](configuration.md) + [Troubleshooting](troubleshooting.md) |
| Performance tuning | [Configuration](configuration.md) + [Troubleshooting](troubleshooting.md) |
| Serialization options | [Configuration](configuration.md) |

## Additional Resources

- **Package README**: [../src/GlacialCache.PostgreSQL/README.md](../src/GlacialCache.PostgreSQL/README.md) - Quick reference and package overview
- **Root README**: [../README.md](../README.md) - Repository overview
- **Examples**: [../examples/README.md](../examples/README.md) - Runnable code samples
- **Benchmarks**: [../src/GlacialCache.Benchmarks/README.md](../src/GlacialCache.Benchmarks/README.md) - Performance testing

## Documentation Standards

### Schema and Table Name Conventions

Throughout these docs, we use the **default values** for schema and table names unless explicitly noted:

- **Default schema**: `public`
- **Default table**: `glacial_cache`

When examples show custom names (e.g., `cache.entries`), they are clearly marked as customizations.

### Code Examples

All code examples are:
- **Copy-pasteable**: They work as-is or with minimal configuration changes
- **Production-aware**: Include notes about dev vs production considerations
- **Up-to-date**: Match the current API surface of GlacialCache.PostgreSQL

### Framework Support

GlacialCache.PostgreSQL supports:
- **.NET 8.0**
- **.NET 9.0**
- **.NET 10.0**

Examples typically use .NET 8.0+ features and patterns.

## Contributing to Documentation

Found an issue or want to improve the docs? Contributions are welcome!

1. Documentation files are written in Markdown
2. Keep examples practical and tested
3. Maintain consistency with existing style
4. Update cross-references when adding new sections
5. Submit pull requests with clear descriptions

## Feedback

Have questions or suggestions about the documentation? Please:
- Open an issue on GitHub
- Start a discussion in GitHub Discussions
- Submit a pull request with improvements

---

**Happy caching!** 🎉

