import { type FormEvent, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { createListing } from '../../api/listings';
import { ApiError } from '../../api/client';

const LISTINGS_QUERY_KEY = ['listings'];

export function SellPage() {
  const [berryType, setBerryType] = useState('');
  const [farmName, setFarmName] = useState('');
  const [pricePerPint, setPricePerPint] = useState('');
  const [quantityAvailable, setQuantityAvailable] = useState('');
  const [note, setNote] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: () =>
      createListing({
        berryType,
        farmName,
        pricePerPint: Number(pricePerPint),
        quantityAvailable: Number(quantityAvailable),
        note: note.trim() ? note.trim() : null,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: LISTINGS_QUERY_KEY });
      navigate('/market');
    },
    onError: (err) => {
      setErrors(err instanceof ApiError ? err.errors : ['Something went wrong — try again.']);
    },
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setErrors([]);
    mutation.mutate();
  }

  return (
    <section className="sell">
      <div className="panel-card">
        <h2>List your berries</h2>
        <p>Got a surplus from the garden or the orchard? Post a crate and Berrow lists it on the market instantly.</p>
        {errors.length > 0 && (
          <ul className="form-errors">
            {errors.map((error) => (
              <li key={error}>{error}</li>
            ))}
          </ul>
        )}
        <form onSubmit={handleSubmit}>
          <div className="field">
            <label htmlFor="f-name">Berry</label>
            <input
              id="f-name"
              maxLength={40}
              placeholder="e.g. Tayberries"
              value={berryType}
              onChange={(e) => setBerryType(e.target.value)}
            />
          </div>
          <div className="field">
            <label htmlFor="f-farm">Farm or garden</label>
            <input
              id="f-farm"
              maxLength={40}
              value={farmName}
              onChange={(e) => setFarmName(e.target.value)}
            />
          </div>
          <div className="row-2">
            <div className="field">
              <label htmlFor="f-price">Price per pint ($)</label>
              <input
                id="f-price"
                type="number"
                min="0.10"
                step="0.05"
                value={pricePerPint}
                onChange={(e) => setPricePerPint(e.target.value)}
              />
            </div>
            <div className="field">
              <label htmlFor="f-qty">Pints available</label>
              <input
                id="f-qty"
                type="number"
                min="0"
                step="1"
                value={quantityAvailable}
                onChange={(e) => setQuantityAvailable(e.target.value)}
              />
            </div>
          </div>
          <div className="field">
            <label htmlFor="f-note">Note (optional)</label>
            <input
              id="f-note"
              maxLength={80}
              placeholder="Sweet, a little tart, best by Friday"
              value={note}
              onChange={(e) => setNote(e.target.value)}
            />
          </div>
          <button type="submit" className="btn btn-primary" disabled={mutation.isPending}>
            {mutation.isPending ? 'Posting…' : 'Post listing'}
          </button>
        </form>
      </div>
    </section>
  );
}
