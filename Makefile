# Swift Testing in Command Line Tools (without full Xcode) ships as a separate
# framework; the SwiftPM test runner needs a global -F flag, otherwise
# canImport(Testing) == false and tests silently do not run.
FRAMEWORKS = /Library/Developer/CommandLineTools/Library/Developer/Frameworks
TESTFLAGS = -Xswiftc -F -Xswiftc $(FRAMEWORKS)

APP_NAME = Claude Usage Widget
DIST = dist/$(APP_NAME).app

.PHONY: run test app clean

run:
	swift run ClaudeUsageWidget

# make test              — run all tests
# make test FILTER=Usage — run only suites/tests matching FILTER
test:
	swift test $(TESTFLAGS) $(if $(FILTER),--filter $(FILTER))

app:
	swift build -c release
	rm -rf "$(DIST)"
	mkdir -p "$(DIST)/Contents/MacOS"
	cp .build/release/ClaudeUsageWidget "$(DIST)/Contents/MacOS/ClaudeUsageWidget"
	cp Resources/Info.plist "$(DIST)/Contents/Info.plist"
	codesign --force --sign - "$(DIST)"
	@echo "Done: $(DIST)"

clean:
	rm -rf .build dist
