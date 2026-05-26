# Form Automation Template
# Demonstrates fill (clear+replace) vs type (append) for input fields.

# === 1. Discover and snapshot the form window ===
seeless-uia windows
seeless-uia snapshot w1 -i --json
# Parse JSON response. Identify input field refs.
# Example: textbox "Email" [ref=e3], textbox "Password" [ref=e5], button "Submit" [ref=e7]

# === 2. Fill inputs ===
# fill = clear existing content, then insert new value.
# Use for fields where you want to replace existing content.
seeless-uia fill @e3 "user@example.com"
seeless-uia fill @e5 "securePassword123"

# === 3. Type into inputs (alternative to fill) ===
# type = append characters without clearing first.
# Use when you want to add to existing content, not replace.
# seeless-uia type @e3 "additional text" --delay 50

# === 4. Interact with controls ===
# Check a checkbox
seeless-uia check @e6
# Select a dropdown option
seeless-uia select @e8
# Click submit
seeless-uia click @e7

# === 5. Verify ===
# Wait for confirmation
seeless-uia wait --text "Success" --timeout 10000
seeless-uia get text @e1

# === 6. Cleanup ===
seeless-uia close
