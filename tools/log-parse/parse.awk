BEGIN {
	FS="\t";
}
NF == 3 {
print $1,$2,$3
}
