namespace PrReviewer.Tests.Helpers;

/// <summary>
/// Small, hand-written diffs covering the shapes the parser has to handle.
/// </summary>
public static class SampleDiffs
{
    /// <summary>
    /// Two hunks in one file (with a removed line and an empty context line
    /// that lost its leading space), a new file, and a deleted file.
    /// </summary>
    public static readonly string Mixed = string.Join("\n",
        "diff --git a/src/A.cs b/src/A.cs",
        "index 1111111..2222222 100644",
        "--- a/src/A.cs",
        "+++ b/src/A.cs",
        "@@ -10,4 +10,4 @@ class A",
        " keep10",
        "-old11",
        "+new11",
        "",
        " keep13",
        "@@ -50,2 +50,3 @@",
        " keep50",
        "+added51",
        " keep52",
        @"\ No newline at end of file",
        "diff --git a/db/B.sql b/db/B.sql",
        "new file mode 100644",
        "--- /dev/null",
        "+++ b/db/B.sql",
        "@@ -0,0 +1,2 @@",
        "+line1",
        "+line2",
        "diff --git a/Old.cs b/Old.cs",
        "deleted file mode 100644",
        "--- a/Old.cs",
        "+++ /dev/null",
        "@@ -1,2 +0,0 @@",
        "-gone1",
        "-gone2",
        "");

    /// <summary>The klelab test PR: three planted bugs in two files.</summary>
    public static readonly string Klelab = string.Join("\n",
        "diff --git a/contact.php b/contact.php",
        "index a723b4f..cb15fe2 100644",
        "--- a/contact.php",
        "+++ b/contact.php",
        "@@ -153,7 +153,7 @@ if ($link_count > $MAX_LINKS) {",
        " ",
        " // Strip anything that could be used for header injection (newlines) in",
        " // fields that end up influencing headers.",
        "-$safe_name  = str_replace([\"\\r\", \"\\n\"], '', $name);",
        "+$safe_name  = $name;",
        " $safe_email = str_replace([\"\\r\", \"\\n\"], '', $email);",
        " ",
        " $subject = \"New message from $SITE_NAME\";",
        "diff --git a/script.js b/script.js",
        "index 4c183b8..fc64885 100644",
        "--- a/script.js",
        "+++ b/script.js",
        "@@ -26,8 +26,9 @@ form.addEventListener(\"submit\", function (event) {",
        "       return response.json();",
        "     })",
        "     .then(function (result) {",
        "+      console.log(\"form result\", result);",
        "       statusText.textContent = result.message;",
        "-      if (result.success) {",
        "+      if (!result.success) {",
        "         form.reset();",
        "       }",
        "     })",
        "");
}
