# Window Automation Template
# Replace placeholders with actual values from snapshot output.

# === 1. Discover windows ===
seeless-uia windows
# Find the target window's wN ref in the output table.
# Example: w1  Notepad  *Untitled - Notepad

# === 2. Target and snapshot ===
# Replace w1 with the actual ref from step 1.
seeless-uia snapshot w1 -i --json
# Parse the JSON response to find element refs.
# Example: {"refs":{"e3":{"role":"button","name":"OK"}}}

# === 3. Interact ===
# Replace @e3 with the actual ref from step 2.
seeless-uia click @e3

# === 4. Verify state ===
seeless-uia snapshot -i --json
# Check the new snapshot for expected state changes.

# === 5. Cleanup ===
seeless-uia close
