#!/bin/sh
# Booking for the shared master editor (port 7800): take it before compiling, saving a scene or entering Play, free it
# after. The lock lives in Library/ (git-ignored, kept when the editor restarts; Temp/ is wiped then).
#   sh Tools/book7800.sh take <name>   books it, or says who has it (a booking older than 60 minutes counts as stale)
#   sh Tools/book7800.sh free <name>   gives it back (only the holder's booking is removed)
#   sh Tools/book7800.sh show          who has it, since when
LOCK="$(dirname "$0")/../Library/book7800.lock"
cmd="$1"; who="$2"
now=$(date +%s)
held() { [ -f "$LOCK" ] && cut -d' ' -f1 "$LOCK"; }
since() { [ -f "$LOCK" ] && cut -d' ' -f2 "$LOCK"; }
case "$cmd" in
  take)
    [ -z "$who" ] && { echo "usage: take <name>"; exit 2; }
    h=$(held); s=$(since)
    if [ -n "$h" ] && [ "$h" != "$who" ] && [ $((now - s)) -lt 3600 ]; then
      echo "busy: $h since $(( (now - s) / 60 )) min ago"; exit 1
    fi
    echo "$who $now" > "$LOCK"; echo "yours: $who" ;;
  free)
    h=$(held)
    if [ "$h" = "$who" ]; then rm -f "$LOCK"; echo "free"; else echo "not yours (held by ${h:-nobody})"; exit 1; fi ;;
  show)
    h=$(held); [ -n "$h" ] && echo "$h since $(( (now - $(since)) / 60 )) min ago" || echo "free" ;;
  *) echo "usage: take|free <name>, show"; exit 2 ;;
esac
