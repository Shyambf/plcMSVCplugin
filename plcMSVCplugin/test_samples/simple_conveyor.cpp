#include <iostream>

using namespace std;

chain Producer(funnel(out, int) * V) {
  int val = 42;
  Sleep(200);
  if (V->empty())
     *V = val;
  cout << "Producer: sent " << val << " to funnel" << endl;
}

chain Consumer(funnel(in, int) * V) {
  int val = 0;
  while (V->empty())
    _Yield();
  cout << "Consumer: received " << **V << " from funnel" << endl;
}

int main() {
  cout << "=== Planning C 2.0: Parallel Conveyor Demo ===" << endl;

  funnel(in, int) * _IN   = new funnel(in, int)("AB");
  funnel(out, int) * _OUT = new funnel(out, int)("AB");

  cout << "Starting parallel chain with funnel data transfer..." << endl;
  plan_parallel_chain(1, Producer(_OUT), Consumer(_IN));

  cout << "=== Execution finished successfully! ===" << endl;

  return 0;
}