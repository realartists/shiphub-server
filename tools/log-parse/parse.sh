#!/bin/bash

find . -type f \
  | sed "s/%3A/:/g" \
  | sed "s/%3F/?/g" \
  | sed "s/\.\///" \
  | sed "s/_/\t/" \
  | sed "s/Z\//Z\t/" \
  | sed "s/\.log$//" \
  | awk -f parse.awk \
  | sort -n
