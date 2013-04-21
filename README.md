Test Alliance Intel Map Reporting Tool
======================================

Scans the text logs geneated by the EVE Online client and forwards the contents to
the [Test Alliance Intel Map](http://map.pleaseignore.com/).  The client looks for
changes in the chat log directory and manages itself automatically.  It minimizes
out of the way to the notification area.

Development To-Do List
----------------------
* The monitoring component is a (mostly) opaque, monolithic block of
  `internal`-based spaghetti.  It needs to be broken up into something more
  compatible with unit testing.
* Need to more thoroughly refine the file selection and switching logic.
  Performance optimizations on the part of Windows make this difficult.
* The user interface needs a lot of love.
