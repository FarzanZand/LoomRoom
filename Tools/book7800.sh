#!/bin/sh
# Booking for the shared master editor (port 7800). The one booking with its queue lives outside the repo, in
# C:/Game projects/editor-booking/ (kept across editor restarts); this only passes everything on to it, so both paths
# share one lock.
#   sh Tools/book7800.sh take <name> "<what>"   yours, or WAIT and you are queued
#   sh Tools/book7800.sh free <name>            give it back
#   sh Tools/book7800.sh show                   who holds it and who waits
exec sh "/c/Game projects/editor-booking/book7800.sh" "$@"
