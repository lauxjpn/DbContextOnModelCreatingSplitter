# what's different about this fork:
### Fixes:
 - The using statement for contextNamespace had two semicolons at the end of line.
 - The modelBuilder not capture all lambda.

### News:
 - Suffix for the generated configuration files, by using the 'suffix' option at command-line ('-s').
 - Code that invokes configuration class is inserted at OnModelCreating event, but can be ignored by 'no-replace' option at command-line ('-r').
 - The configuration class can now be embedded into entity model file, by using the 'embed-config' option at command-line ('-e').
---

# DbContext OnModelCreating Splitter
Command line tool that splits FluentAPI entity definitions from `DbContext.OnModelCreating()` into separate configuration files.

This tool can be used as a workaround for https://github.com/aspnet/EntityFrameworkCore/issues/8434.

### Usage
```
  -c, --dbcontext    Required. Path the the DbContext file

  -o, --outdir       Output path for the generated configuration files

  -n, --namespace    Namespace for the generated configuration classes

  -s, --suffix       Suffix for the generated configuration files
  
  -B, --no-backup    Don't keep a copy of the original DbContext file

  --help             Display this help screen.

  --version          Display version information.
```

### Example
```
DbContextOnModelCreatingSplitter -c Data\MyContext.cs -o Data\Configurations -n MyNamespace.Data.Configurations
```

### Note
A backup of the original DbContext file is created by default.

### Download
You can [download](https://github.com/lauxjpn/DbContextOnModelCreatingSplitter/releases/download/v1.0.1/DbContextOnModelCreatingSplitter_1.0.1.zip) the binaries from the latest [release](https://github.com/lauxjpn/DbContextOnModelCreatingSplitter/releases). 

### License
[MIT](https://github.com/lauxjpn/DbContextOnConfiguringSplitter/blob/master/LICENSE)
