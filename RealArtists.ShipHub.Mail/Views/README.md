# Email Templates

We use RazorGenerator.  It's a Visual Studio extension that generates C#
classes every time you save a template.

To install the extension, go to Tools -> Extensions and Updates.  Choose
Online, and search for Razor Generator.

Note that you do not need the extension to use the templates.  You only
need the extension to edit or add templates.

There is a version of RazorGenerator that does not require installing a
VS extension (RazorGenerator.MSBuild), but I found more headaches down
that path and retreated.

# Build Warnings and Errors in VS

VS will probably show errors whenever you have a `.cshtml` file open.
Usually it's the following --

```
The name "Context" does not exist in the current context.
```

When you close the `.cshtml` file, the error will go away.  You'll have
to ignore these for now.

# Adding New Templates

After you create the `.cshtml` file, you must change the properties so
RazorGenerator is run on this file.

On the `.cshtml` file, right click and choose Properties.  Then, set
the following options:

 * Build Action: None
 * Copy to Output Directory: Do not copy
 * Custom Tool: RazorGenerator


