#!/bin/bash
# Session-start hook for Claude Code on the web.
#
# Installs the obra/superpowers plugin so skills like
# /superpowers:brainstorming, /superpowers:writing-plans, /tdd, etc. are
# available in every session against this repo.
#
# Local Claude Code sessions have their own persistent plugin state, so
# this hook is a no-op there — it only runs in remote (web) sessions.

set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# `claude plugin marketplace add` is idempotent (no-op if the marketplace
# is already registered). `claude plugin install` likewise re-installs
# without error if the plugin is already present.
claude plugin marketplace add obra/superpowers
claude plugin install superpowers@superpowers-dev
