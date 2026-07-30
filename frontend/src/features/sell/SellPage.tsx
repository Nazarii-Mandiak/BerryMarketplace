import { type ChangeEvent, type FormEvent, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { createListing, deleteListingPhoto, getListing, updateListing, uploadListingPhoto } from '../../api/listings';
import { getAiStatus, suggestListing } from '../../api/ai';
import { ApiError } from '../../api/client';
import { useToast } from '../../components/ToastProvider';

const LISTINGS_QUERY_KEY = ['listings'];

export function SellPage() {
  const { id } = useParams<{ id: string }>();
  const isEditing = Boolean(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const [berryType, setBerryType] = useState('');
  const [farmName, setFarmName] = useState('');
  const [pricePerKg, setPricePerKg] = useState('');
  const [quantityAvailableKg, setQuantityAvailableKg] = useState('');
  const [note, setNote] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [aiEnabled, setAiEnabled] = useState(false);
  const [aiReasoning, setAiReasoning] = useState<string | null>(null);
  const [photoFile, setPhotoFile] = useState<File | null>(null);
  const [photoPreviewUrl, setPhotoPreviewUrl] = useState<string | null>(null);
  const [hasExistingPhoto, setHasExistingPhoto] = useState(false);
  const [removePhoto, setRemovePhoto] = useState(false);

  const { data: existingListing } = useQuery({
    queryKey: ['listing', id],
    queryFn: () => getListing(id!),
    enabled: isEditing,
  });

  useEffect(() => {
    if (!existingListing) return;
    setBerryType(existingListing.berryType);
    setFarmName(existingListing.farmName);
    setPricePerKg(String(existingListing.pricePerKg));
    setQuantityAvailableKg(String(existingListing.quantityAvailableKg));
    setNote(existingListing.note ?? '');
    setHasExistingPhoto(existingListing.hasPhoto);
  }, [existingListing]);

  useEffect(() => {
    let cancelled = false;
    getAiStatus()
      .then((status) => {
        if (!cancelled) setAiEnabled(status.enabled);
      })
      .catch(() => {
        if (!cancelled) setAiEnabled(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!photoFile) {
      setPhotoPreviewUrl(null);
      return;
    }
    const url = URL.createObjectURL(photoFile);
    setPhotoPreviewUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [photoFile]);

  const mutation = useMutation({
    mutationFn: () => {
      const payload = {
        berryType,
        farmName,
        pricePerKg: Number(pricePerKg),
        quantityAvailableKg: Number(quantityAvailableKg),
        note: note.trim() ? note.trim() : null,
      };
      return isEditing ? updateListing(id!, payload) : createListing(payload);
    },
    onSuccess: async (listing) => {
      queryClient.invalidateQueries({ queryKey: LISTINGS_QUERY_KEY });
      // The listing itself is already saved at this point - a photo failure here must not
      // read as the whole save having failed.
      if (photoFile) {
        try {
          await uploadListingPhoto(listing.id, photoFile);
        } catch {
          showToast('Listing saved, but the photo failed to upload — try again from Edit.');
        }
      } else if (isEditing && removePhoto) {
        try {
          await deleteListingPhoto(listing.id);
        } catch {
          showToast('Listing saved, but removing the photo failed — try again from Edit.');
        }
      }
      navigate('/market');
    },
    onError: (err) => {
      setErrors(err instanceof ApiError ? err.errors : ['Something went wrong — try again.']);
    },
  });

  const aiMutation = useMutation({
    mutationFn: () =>
      suggestListing({
        berryType,
        farmName,
        pricePerKg: pricePerKg.trim() ? Number(pricePerKg) : null,
        quantityAvailableKg: quantityAvailableKg.trim() ? Number(quantityAvailableKg) : null,
        note: note.trim() ? note.trim() : null,
      }),
    onSuccess: (suggestion) => {
      setNote(suggestion.improvedDescription);
      setPricePerKg(String(suggestion.suggestedPricePerKg));
      setAiReasoning(suggestion.reasoning);
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

  function handleImproveWithAi() {
    setErrors([]);
    aiMutation.mutate();
  }

  function handlePhotoChange(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0] ?? null;
    setPhotoFile(file);
    if (file) setRemovePhoto(false);
  }

  function handleRemovePhoto() {
    setPhotoFile(null);
    setRemovePhoto(true);
  }

  const showExistingPhoto = isEditing && hasExistingPhoto && !removePhoto && !photoPreviewUrl;

  return (
    <section className="sell">
      <div className="panel-card">
        <h2>{isEditing ? 'Edit your listing' : 'List your berries'}</h2>
        <p>
          {isEditing
            ? 'Update the details buyers see on the market.'
            : "Got a surplus from the garden or the orchard? Post a crate and Berrow lists it on the market instantly."}
        </p>
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
              required
            />
          </div>
          <div className="field">
            <label htmlFor="f-farm">Farm or garden</label>
            <input
              id="f-farm"
              maxLength={40}
              value={farmName}
              onChange={(e) => setFarmName(e.target.value)}
              required
            />
          </div>
          <div className="row-2">
            <div className="field">
              <label htmlFor="f-price">Price per kg ($)</label>
              <input
                id="f-price"
                type="number"
                min="0.10"
                step="0.01"
                value={pricePerKg}
                onChange={(e) => setPricePerKg(e.target.value)}
                required
              />
            </div>
            <div className="field">
              <label htmlFor="f-qty">Kilograms available</label>
              <input
                id="f-qty"
                type="number"
                min="0"
                step="0.01"
                value={quantityAvailableKg}
                onChange={(e) => setQuantityAvailableKg(e.target.value)}
                required
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
          <div className="field">
            <label htmlFor="f-photo">Photo (optional)</label>
            {photoPreviewUrl && <img src={photoPreviewUrl} alt="" className="photo-preview" />}
            {showExistingPhoto && (
              <>
                <img src={`/api/listings/${id}/photo`} alt="" className="photo-preview" />
                <button type="button" className="btn btn-ghost" onClick={handleRemovePhoto}>
                  Remove photo
                </button>
              </>
            )}
            <input id="f-photo" type="file" accept="image/jpeg,image/png,image/webp" onChange={handlePhotoChange} />
          </div>
          {aiEnabled && (
            <button
              type="button"
              className="btn btn-ghost"
              disabled={aiMutation.isPending || !berryType.trim() || !farmName.trim()}
              onClick={handleImproveWithAi}
            >
              {aiMutation.isPending ? 'Thinking…' : 'Improve with AI'}
            </button>
          )}
          <button type="submit" className="btn btn-primary" disabled={mutation.isPending}>
            {mutation.isPending ? (isEditing ? 'Saving…' : 'Posting…') : isEditing ? 'Save changes' : 'Post listing'}
          </button>
        </form>
        {aiReasoning && <p className="ai-reasoning">{aiReasoning}</p>}
      </div>
    </section>
  );
}
