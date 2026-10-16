#include <iostream>
#include <iomanip>

using namespace std;

#include "memoization.h"

#pragma memoization(i)
double fibb(int a) {
  if (a < 3) return 1;
  else return fibb(a - 1) + fibb(a - 2);
}

int main() {
  cout << "=== Planning C 2.0: Memoization Demo ===" << endl << endl;
  cout << "Fibonacci(10) = " << setprecision(16) << fibb(10) << endl;
  cout << "Fibonacci(20) = " << setprecision(16) << fibb(20) << endl;
  cout << "Fibonacci(35) = " << setprecision(16) << fibb(35) << endl << endl;
  cout << "=== Execution finished successfully! ===" << endl;
  return 0;
}
